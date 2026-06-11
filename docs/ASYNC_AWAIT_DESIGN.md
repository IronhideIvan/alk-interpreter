# Async/Await Design Decisions

This document records the resolved design decisions for adding `async`/`await`
support to the ALKScript interpreter, reached through design discussion before
implementation began. It exists to capture *why* each choice was made, not just
*what* was chosen — so future implementation work (and future revisits of this
area) doesn't have to re-derive the reasoning.

## Use case and core requirements

The motivating scenario is **single-threaded game scripting**:

- Calling an async native operation (e.g., "move object to point") starts a
  real game-side effect that may span many game-loop ticks/frames.
- `await` suspends the script until that operation completes — the script and
  the game loop never run concurrently (strict turn-taking).
- An async operation must **not** begin running merely because it was called —
  only when it is `await`ed, or the script finishes execution entirely
  (lazy/deferred start).

  > **Why "or the script finishes execution entirely" is necessary, not
  > redundant:** it might look like a contradiction — "if the script has
  > finished, how can anything related to it start?" — but the operation is a
  > host-side game effect, not something that needs the script to stay alive
  > to make progress. The clause is what makes **fire-and-forget** possible:
  > a script calls `moveTo(npc, x, y)` without `await`ing it, does other
  > things, and ends. Without this clause, "never start unless awaited" would
  > make an un-awaited async call a silent no-op — fire-and-forget would be
  > *impossible*, and "I called `moveTo`, so the NPC moves" would silently not
  > hold. With it, reaching the end of the script is the signal that converts
  > an un-awaited call into a fire-and-forget start (handled by the `Discard`
  > mechanism in decision #10): the operation is handed off and proceeds
  > independently in the game world, exactly like a real effect that doesn't
  > need a UI button held down to keep running once triggered.

- Cancelling a running script must also cancel any in-flight asynchronous
  tasks it started.
- Many scripts may be running/awaiting concurrently — thread-per-script is
  unacceptable.
- Suspended script state must be serializable, so a game can pause, save,
  reload, and resume scripts that were mid-`await`.

## Resolved decisions

### 1. Suspension mechanism

Rejected a new `SignalKind.Await` — `Signal`-based unwinding can't preserve
and later resume mid-expression evaluation state. Converged instead on making
the evaluator `async`/`Task`-based, leveraging C#'s compiler-generated
continuation-passing-style (CPS) state machines for suspension.

`Signal` (`Return`, `Thrown`, and a new `Cancelled`) is **kept as-is** —
suspension and unwinding are orthogonal concerns. `ExecuteTry` already threads
any pending signal through `finally` unconditionally and only special-cases
`Thrown` for `catch`, so a `Cancelled` kind automatically bypasses `catch`
while still running `finally`, with no code changes required there.

### 2. Scaling to many concurrent scripts

Rejected thread-per-script (OS thread cost too high for "potentially many
scripts awaiting"). Adopted a single custom `SynchronizationContext`/
`ScriptScheduler`, pumped once per game-loop tick (`Pump()`), that multiplexes
every script's `await` continuations onto one host thread — mirroring the
WinForms/WPF/Unity main-thread dispatcher pattern.

### 3. Voluntary yielding (`pause`)

Endorsed a `pause(frames)` primitive for scripts that would otherwise starve
the frame. It is exposed as a **standard-library function** (`async native
pause(int frames)`), not a new keyword. Reasons: preserves "`await` is the one
true suspension primitive" as a language invariant, avoids grammar bloat, stays
more composable/extensible, and avoids a naming collision with generator-style
"yield" semantics in other languages.

The implementation is entirely the **host's responsibility**, handled inside
their `IAsyncOperationBinder.Start` like any other async native operation. The
standard library only supplies the declaration; the host wires up the behavior.
This requires no changes to the scheduler — `Pump()`, called once per game-loop
tick, already is the frame boundary. A host implementing `pause(n)` counts how
many more `Pump()` calls should elapse before settling the operation's
`TaskCompletionSource`, because the host controls when `Pump()` is called and
therefore knows exactly when each frame ends. The scheduler needs no
`AdvanceFrame()` or other frame-awareness API.

### 4. `await`-only-in-`async` enforcement

Enforced at **parse time** (consistent with how the spec documents the rule
and with general precedent for catching structural errors early).

See [Parsing-context tracking](#9-parsing-context-tracking-design) below for
the proposed mechanism.

### 5. Script identity / lifecycle

Model is **one compiled program, run by many concurrent instances** — i.e.
compilation is shared; each running script is its own lightweight instance
with its own suspended state.

### 6. Frame semantics of `pause(n)`

Defined guarantee: after `pause(1)`, the script resumes seeing **game state as
of the next completed simulation step**. (Generalizes to `pause(n)` waiting
for `n` completed steps.)

### 7. Scheduling order

Chosen: **deterministic, stable ordering** — e.g. registration/insertion order
via an ordered collection, not hash-based iteration. Rationale ("cost
asymmetry"): guaranteeing this now is cheap; retrofitting it later, once
content has come to depend on incidental ordering, would be expensive and
likely breaking.

### 8. Host integration documentation

Confirmed: a **"Host Integration Guide"** companion document should exist,
covering the `IAsyncOperationBinder`/scheduler contract, scheduling-order
guarantees, frame semantics, and the save/load (replay) contract described
below.

### 9. Parsing-context tracking design

No existing precedent in the parser for "track context while descending into
nested constructs." Since a similar need will arise for a future
`break`/`continue`-must-be-in-a-loop check, the design generalizes to a single
reusable mechanism: a small `ParsingContext` helper holding one flag/counter
per context kind, exposing `IDisposable`-returning `EnterX()` scope guards
that save and auto-restore the previous state, and `InX` query properties:

```csharp
private sealed class ParsingContext
{
  private bool _inAsyncBody;
  private int _loopDepth;

  public IDisposable EnterAsyncBody()
  {
    bool previous = _inAsyncBody;
    _inAsyncBody = true;
    return new Restorer(() => _inAsyncBody = previous);
  }

  public IDisposable EnterLoop()
  {
    _loopDepth++;
    return new Restorer(() => _loopDepth--);
  }

  public bool InAsyncBody => _inAsyncBody;
  public bool InLoop => _loopDepth > 0;
}
```

Used at the relevant parse sites:

```csharp
using (_parsingContext.EnterAsyncBody())
{
  body = ParseFunctionOrMethodBody(isNative, "function");
}
```

```csharp
case AwaitExpr when !_parsingContext.InAsyncBody:
  throw Error(keyword, "'await' is only valid inside an 'async' function or method.");
```

Saving/restoring (rather than just setting a flag) correctly handles a
non-`async` function nested inside an `async` one. The same pattern extends
to `break`/`continue` (`InLoop`) and any future context-sensitive construct
(e.g. `switch`) by adding one field and one `EnterX`/`InX` pair.

### 10. Unobserved faults

Under the lazy-start model, an operation can't fault before it starts, and it
only starts via `await` (whose fault is immediately surfaced as
`Signal.Thrown` and is therefore "observed") or the end-of-script `Discard`
of anything left pending. So an *unobserved* fault can only occur in the
post-script fire-and-forget tail — at which point the script context no
longer exists, so the fault must be reported to the **host**. `Discard`'s
signature should be enriched accordingly:

```csharp
void Discard(PendingOperation operation, Action<ALKScriptValue> onUnobservedFault);
```

A follow-up question — "could a mid-script fault on an un-awaited sibling slip
past unobserved?" — is resolved by decision #11 below: because `whenAll` waits
for every member to settle before resolving, that scenario cannot occur. There
is no "script already moved on, then a sibling faults into the void" case to
handle separately.

### 11. `whenAll` fault and cancellation policy

- **Siblings are not cancelled when one member faults** — every member runs to
  completion regardless of how others resolve.
- **All faults are aggregated** and surfaced to the script as a single
  catchable error from the `await` (mirroring .NET's `Task.WhenAll` +
  `AggregateException` — same shape, validated prior art).
- These two choices together imply `whenAll` cannot resolve until every member
  has settled, which is what eliminates the "mid-script unobserved fault"
  scenario from #10.
- Additionally, **every individual fault should also be reported to the
  host** (e.g. for logging/telemetry) — *in addition to*, not instead of, the
  aggregate the script can catch. This is a complementary hook in the same
  family as `onUnobservedFault`, just triggered on a different schedule.

### 12. `whenAny` / race combinators

**Rejected** — incompatible with the run-to-completion guarantee established by
decision #11. `whenAny` resolves as soon as the first member settles while its
siblings are still running; decision #11 requires every member to run to
settlement before the combinator resolves. These are mutually exclusive: there
is no definition of `whenAny` that satisfies run-to-completion. The feature is
not deferred — it is ruled out by a committed design choice.

### 13. `await [a, b]` syntax *(implemented)*

Defined as **pure sugar for `await Task.whenAll([a, b])`**, generalized: any
`Task<T>[]`/`Task[]` value passed to `await` is treated as `whenAll` of that
collection. Array literal syntax (`[expr, ...]`) is parsed in `ParsePrimary()`
and `await` on an `ArrayValue` is routed to `EvalWhenAll` — both are fully
implemented end-to-end.

This was chosen over an alternative "awaiting one starts all pending operations"
rule, which was rejected for introducing non-local "action at a distance"
behavior, an ill-defined notion of "what counts as pending," and significant
complications for the replay log (implicit membership-dependent batches versus
clean per-operation log units). The sugar achieves conciseness while remaining
explicit, local, and replay-friendly — and better honors the original lazy-start
requirement (read as "*that* operation, on *that* `await`").

### 14. Combinator cancellation propagation

A shared `CancellationToken` reaching every member operation handles telling
the *operations* to wind down. The one addition: the combinator's own
suspension point — the thing the script is parked on while waiting for the
joint result — must **itself** be an ordinary, cancellation-aware suspension
(observing the token / raising `Signal.Cancelled`), not something that merely
waits for member operations to fault their way to a resolution. In practice
this should fall out for free if `whenAll`'s wait is implemented as just
another `await`-shaped suspension subject to the same `Signal.Cancelled`
handling as everything else — it's a "make sure it isn't accidentally exempt"
note, not extra machinery to build.

### 15. Replay-log scope ("which calls get logged")

Not literally "every native call." The precise rule: **log any call whose
result could legitimately differ between the original run and a replay**.
This is a strict superset of "every `await`ed async operation" (those always
qualify — their results come from the asynchronous game world) but excludes
calls that are pure functions of their arguments (`Math.sqrt`, string
formatting, array indexing, …), which can simply be re-executed during replay
with identical results at zero log cost.

The interesting middle ground is **synchronous** natives that are still
non-deterministic (wall-clock time, randomness, live mutable game state) —
these need logging despite not being `async`. This mirrors the
activities-vs-deterministic-workflow-code split used by Temporal/Durable
Functions.

### 16. How "replay-sensitive" is declared

**Not** a grammar feature (no `[NonDeterministic]`-style modifier in `.alk`
source). The classification has zero effect on script-visible behavior — a
logged and an unlogged native call look and return identically; only what the
*runtime* persists changes. That makes it purely a host/runtime contract,
belonging alongside `IAsyncOperationBinder` and the binding registries
(`ScriptNativeBindings` / `ScriptNativeMethodBindings`), not in the language.

Concretely: when the host registers the implementation for a `native`
declaration, it *also* supplies metadata (e.g. an `IsReplaySensitive` flag or
a parallel registration table) declaring whether that implementation's results
must be recorded for replay. This keeps the grammar entirely script-author
facing, avoids introducing the language's first attribute/decorator construct,
and lets the host re-classify a native later (e.g. discovering a "pure" one
secretly reads live state) without touching or recompiling any script.

Note `async` already covers half of this for free: every `async native` is
inherently replay-sensitive by definition, so the new metadata only needs to
cover the narrower set of non-deterministic *synchronous* natives.

### 17. Save/load of suspended scripts (architecture-defining)

Confirmed as a real, hard requirement — and identified as
**architecture-changing**: compiler-generated `async`/`Task` continuations are
opaque, version-dependent, and not serializable.

Adopted solution: a **record-and-replay model**, à la Temporal / Azure
Durable Functions. Persist a flat, ordered log of `(operation, result-or-fault)`
pairs per script instance. To resume after reload, replay the script from the
start, short-circuiting each `Suspend` against the recorded log (returning the
recorded result/fault immediately) until the log is exhausted, then continue
live from that point — pumped by the scheduler as normal.

This requires two supporting constraints, both addressed above:
- **Deterministic script logic between `await`s** — replay must reproduce the
  same sequence of operations given the same log, which is what makes
  decision #15/#16 (precisely scoping what gets logged) load-bearing.
- **Standard library design surface** — combinators (`whenAll`, deferred
  `whenAny`) belong in the standard library (decision below), not language
  grammar, keeping the deterministic/replay-relevant surface small and
  centrally controlled.

### 18. `Task` surface beyond `await`

Combinators and the broader `Task`/`Task<T>` API surface belong in the
**standard library**, not the language grammar — the same category as
`Array`, `Error`, `HttpClient`, which are already `native`-backed stdlib
rather than built-in syntax.

## Summary of what's settled

All originally-identified open questions have been resolved:

1. Suspension mechanism → `async`/`Task`-based evaluator, `Signal` unchanged
   plus new `Cancelled` kind
2. Concurrency model → single-threaded custom scheduler/`SynchronizationContext`
3. Voluntary yielding → `pause(frames)` as a standard-library `async native`
   declaration; implementation is the host's responsibility inside their
   `IAsyncOperationBinder`; `Pump()` already serves as the frame boundary so
   no scheduler changes are required
4. `await`-in-`async` enforcement → parse time, via reusable `ParsingContext`
5. Script identity → one compiled program, many concurrent instances
6. `pause` frame semantics → resumes at next completed simulation step
7. Scheduling order → deterministic, stable (insertion order)
8. Host docs → "Host Integration Guide" companion doc planned
9. Unobserved faults → routed to host via enriched `Discard(... ,
   onUnobservedFault)`; mid-script case eliminated by `whenAll` semantics
10. `whenAll` policy → run-to-completion + aggregate faults to script,
    individual faults also reported to host
11. `whenAny` → rejected; incompatible with the run-to-completion guarantee
12. `await [a, b]` → sugar for `await Task.whenAll([a, b])`
13. Combinator cancellation → shared token to members + cancellation-aware
    combinator suspension
14. Replay log scope → log non-deterministic results only (all async +
    non-deterministic sync natives), not every native call
15. Replay-sensitivity declaration → host/binding-registration metadata, not
    grammar
16. Save/load of suspended scripts → record-and-replay model
17. `Task` API surface → standard library, not grammar

Implementation may now proceed against this design.

## Addendum: `thunk`/`thunk<T>` revision (post-implementation)

After implementing the design above, a follow-up review concluded that
ALKScript scripts are single-threaded and **cannot create a deferred
operation themselves** — the only source of one is a `native` declaration
provided by the embedding host. Given that, the original `async` modifier on
script-defined functions/methods/lambdas (eager body execution + automatic
`TaskValue`-wrapping of the result, decision #1 above) carried no real
meaning: any function can `await` a deferred value it received and just
return the unwrapped `T`.

This revision **removes** that eager-wrapping behavior and the `async`
keyword entirely:

- The `async` keyword is removed from the grammar (lexer, parser, AST). There
  is no "this function is async" declaration anymore.
- The wrapper type for a deferred operation becomes an explicit, writable,
  type-erased return type: **`thunk`/`thunk<T>`** (a reserved keyword, like
  `lambda<...>`). Only `native` declarations may declare a `thunk`/`thunk<T>`
  return type — script functions never construct one, but may *forward* one
  they received by declaring the same return type and returning it
  un-awaited.
- `await` becomes a universally-valid prefix operator, usable in the body of
  any function/method/lambda (including top level), with no "must contain
  `await`" parse-time check.
- `CallInvoker.InvokeFunction` no longer special-cases anything based on
  "is async" — every script function call runs its body to completion and
  returns its result directly (the old "non-async" path is now the only
  path). Concurrency/suspension is provided exclusively by `await`ing
  `native`-sourced `thunk`/`thunk<T>` values (`ThunkValue`/
  `PendingOperationValue`), which is unchanged from decisions #1–#17 above
  apart from naming.
- Renamed for consistency with the new `thunk` terminology: `TaskValue` →
  `ThunkValue`; `TypeName => "Task"` → `TypeName => "thunk"` on both
  `ThunkValue` and `PendingOperationValue`. `await`/`whenAll`/`Discard`
  semantics (decisions #9–#13) are otherwise unchanged — they already
  dispatched on `TypeName == "Task"` (now `"thunk"`) regardless of how the
  value was declared.
- `await` on a non-`thunk` expression remains a lenient no-op (yields the
  value unchanged), confirmed as the intended behavior — see
  `docs/LANGUAGE_SPEC.md` §8.
