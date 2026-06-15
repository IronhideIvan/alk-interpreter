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

## Addendum 2: validating `thunk<T>` resolution against `T`

A further follow-up tightened `await` on a `thunk<T>` (with a concrete `T`):
`ThunkValue`/`PendingOperationValue` are now tagged at construction time with
the declared `T` (`ElementType`), computed from the `thunk<T>` return type of
the `native` declaration that produced them (`FunctionValueFactory`). When
`await` (including `whenAll`'s per-element resolution) resolves such a value,
the result is checked against `T` via `TypeChecking.MatchesType`; a mismatch
throws a `RuntimeException` that is **not** caught by script `try`/`catch` —
it indicates a host/native bug (the binder resolved to the wrong shape), not
a normal runtime fault. Bare `thunk` (`ElementType == null`) has nothing to
validate and remains exactly as lenient as before.

## Addendum 3: `EvaluationCursor` — a synchronous, resumable evaluator spine

Decision #1 above settled on `async`/`Task`-based suspension, reasoning that
the CLR's compiler-generated `async`/`await` state machines could stand in for
a hand-rolled traverser. In practice ALKScript is single-threaded and never
performs real concurrency itself — the `Task`-based spine (`ExpressionEvaluator`/
`StatementExecutor`/`CallInvoker`/`ProgramEvaluator`, ~71 `async` methods, 124
`await` call sites) plus `ScriptScheduler`/`ScheduledTask`'s single-threaded
pump exists purely to give `await fetch()` its suspend/resume semantics. This
indirection also stood in the way of decision #17 (save/load of suspended
scripts): CLR task continuations are opaque and not serializable, so
"suspended state" had no concrete representation to persist.

A parallel implementation, `EvaluationCursor`
(`ALKScript.Interpreter.Evaluator/Cursor/`), replaces the `Task`-based spine
with a hand-rolled, synchronous, resumable traverser:

- Every evaluator method returns `StepResult` (`IsAwaiting` / `Value` /
  `Handle`) instead of `Task<ALKScriptValue>`/`Task`. The mechanical
  propagation pattern at each former `await` site is:
  ```csharp
  var step = Eval(expr, env);
  if (step.IsAwaiting) return step;   // propagate suspension upward
  var x = step.Value!;
  ```
- `Signal` (break/continue/return/thrown/cancelled) is **unchanged** — still
  the orthogonal non-local-exit mechanism, checked after each sub-step exactly
  as before.
- When an `AwaitExpr` is reached on a not-yet-resolved `thunk`/
  `thunk<T>`/`PendingOperationValue` in an allowed position (see
  docs/LANGUAGE_SPEC.md §8.1), `Eval` returns `StepResult.Awaiting(handle)` and
  `EvaluationCursor.Run()` returns `RunResult.Awaiting` — synchronously, with
  no `Task`, no scheduler, no thread involved. The host later calls
  `Resume(value)`/`ResumeFaulted(message)` once the operation settles.
- **Resume trail**: when a top-level run suspends, every enclosing resumable
  construct (block/loop/if/switch/try) records, leaf-to-root, which child
  (statement/iteration/case/region index) it was executing and with which
  `ScriptEnvironment`. `Resume` walks this trail root-to-leaf to fast-forward
  back to the exact suspended statement — re-entering only the chosen
  branches/iterations/regions, without re-evaluating anything already
  evaluated — then substitutes the resumed value for the `await` that
  suspended and continues normal execution.
- `CursorProgramEvaluator` is the module-graph counterpart to
  `ProgramEvaluator`: it runs the global prelude(s) then each module's
  top-level declarations as separate `EvaluationCursor.Start` "segments",
  preserving topological module ordering, import/export binding, and the
  existing record-and-replay log (`OperationLogEntry`/`TryReplayNext`/
  `RecordEntry`) unchanged from decision #17's design.
- `IAsyncOperationBinder` keeps its existing `Task`-returning contract — `Task`
  remains purely a host-boundary type. Only the evaluator's own internal walk
  is now synchronous.

**Scope and current limitations** (tracked for follow-up plans):
- Function, method, and constructor bodies (including `base(...)`
  super-constructor body recursion) may suspend mid-body — the resume trail
  is a single flat list spanning all nested `ExecuteBlock` calls regardless of
  C# call-stack depth, so a suspending nested call simply propagates
  `IsAwaiting` up through `CursorCallInvoker`.
- A suspended constructor's `new` expression is itself re-evaluated on
  resume (per the argument-re-evaluation point below), which would normally
  allocate a second, distinct instance. `CursorCallInvoker.Construct` special-
  cases this: while resuming, it recovers the original instance from the
  resume trail's captured `this` (via `PeekResumeEnvironment`) and reuses it,
  so field mutations performed by the resumed constructor body land on the
  instance the `new` expression ultimately returns.
- Field/static-field initializers cannot suspend — `InitializeFields` runs
  outside the `ExecuteBlock` resume trail, and `CursorCallInvoker` throws if
  an initializer expression returns `IsAwaiting`.
- When a statement containing a suspending call is re-executed on resume,
  `EvalCall`/`EvalNew` re-evaluate the callee and all argument expressions
  from scratch (there is no `HasResumeValue`-style short-circuit for call
  arguments as there is for `AwaitExpr`'s operand). Argument expressions with
  side effects therefore run twice across a suspend/resume cycle — bind such
  expressions to a local first, e.g. `var a = sideEffect(); var y = foo(a);`,
  the same mitigation already recommended for §4's placement restrictions.
  This can be worse than just a double side effect: `IsResuming` stays true
  for the whole re-executed statement (until the trail is fully unwound), so
  if a re-evaluated argument expression itself makes a call, that call's body
  also sees `IsResuming == true` and consumes the resume-trail entry meant
  for the original suspended call. E.g. for `var y = withAwait(sideEffect())`
  where `withAwait` is the one that suspended, on resume `sideEffect()` is
  evaluated first (as the argument), its body unwinds the trail entry meant
  for `withAwait`'s body, and `withAwait` is then entered *fresh* with the
  newly re-evaluated argument — so the resumed `withAwait` body runs with the
  *second* call's argument value, not the first's. Binding the argument to a
  local first avoids this entirely.
- `await [a, b, c]` ("whenAll") may now suspend when one or more elements are
  genuinely in-flight. `EvalWhenAll` classifies each element exactly as the
  old evaluator's `EvalWhenAll` did (replay-log lookup for `PendingOperationValue`,
  else `.Start()`/`.Task` for a live operation, else already-resolved). If
  every element is already settled it resolves synchronously; otherwise it
  returns `StepResult.Awaiting` with a *composite* `AwaitHandle`
  (`AwaitHandle.ForComposite`) whose `CompositeElements` holds one `AwaitElement`
  per array element and whose `CompositeTask` is a `Task.WhenAll` over the live
  elements' tasks — settling, like `Task.WhenAll`, only once *all* of them have
  completed (run-to-completion). The host awaits `CompositeTask` and then calls
  `Resume(NullValue.Instance)` (the passed value is ignored for a composite
  handle; `ResumeFaulted` is invalid for one). `EvaluationCursor.Resume` stashes
  `CompositeElements` for `EvalAwait` to consume via `TryTakeResumeComposite`,
  which re-enters the same `ResolveWhenAll` helper used for the synchronous
  case: it reads each element's settled task, records `OperationLogEntry`
  results/faults to the replay log and reports faults to the host
  (`ReportOperationFaulted`) in source order, aggregates any faults into a
  single `Signal.Thrown` (one message verbatim, or `"Multiple operations
  failed: ..."` for more than one), and otherwise validates each resolved
  value against its declared element type and returns the `ArrayValue`.
- **Capture/Restore ("Phase A", replay-based)**: `EvaluationCursor.Capture()`
  and `CursorProgramEvaluator.Capture()`/`Restore()` snapshot and reconstruct
  a suspended run by reusing the record-and-replay log almost entirely as-is.
  `Capture()` (valid only while `PendingAwait != null`) returns a
  `CursorCaptureState` — `{Phase, ModuleIndex, Log}`, the operation log
  recorded so far via `RecordEntry`. `Restore(graph, ..., state, out result)`
  builds a fresh `CursorProgramEvaluator` seeded with `state.Log` as its
  replay log and calls `Evaluate(graph)` from scratch: every `await`/`whenAll`
  site with a corresponding replay-log entry resolves instantly via
  `TryReplayNext()` until the log is exhausted, at which point the run
  suspends again at the same logical point (`ProgramRunResult.Awaiting`,
  ready for `Resume`/`ResumeFaulted`) — or, if the captured state was the
  run's final suspension, completes (`ProgramRunResult.Completed`), both
  valid outcomes. The caller must supply an equivalent `ModuleGraph` (rebuilt
  from the same source files/module identifiers); Phase A does not serialize
  the AST/module graph itself.

  These types use plain runtime `OperationLogEntry`/`ALKScriptValue` DTOs and
  carry no serialization-format dependency. A separate project,
  `ALKScript.Interpreter.Serialization`, owns converting them to/from a wire
  format: `OperationLogEntrySerializer`/`SerializedValue` (JSON via
  `System.Text.Json`) and the public `CursorStateSerializer.Capture`/`Restore`
  (`byte[]` round trip). Its main limitation is that `SerializedValue`
  restricts values that ever cross the log boundary (an `await`'s recorded
  result) to `IntValue`/`FloatValue`/`StringValue`/`BoolValue`/`NullValue`/
  `ArrayValue` of those (recursively) — any other runtime value
  (`InstanceValue`/`FunctionValue`/`ThunkValue`/`PendingOperationValue`/
  `ClassValue`/etc.) throws `NotSupportedException` at serialize time. A host
  that needs a different wire format, or to lift this restriction, can call
  `CursorProgramEvaluator.Capture`/`Restore` directly and write its own
  serializer against `CursorCaptureState`/`OperationLogEntry`.

  **Deferred follow-ups**:
  - **Phase B** ("structural snapshot") — **implemented**, see below.
  - **Phase C** ("pending operations in locals") — **implemented**, see below:
    reconstructing mid-flight `PendingOperationValue`/`ThunkValue` held in
    *other* local variables (not just the suspending await's own operand) on
    `Restore`.

**Capture/Restore ("Phase B", structural-snapshot)**: a second, additive
Capture/Restore pair giving O(1) restore independent of a run's history
length, and lifting Phase A's primitive-only restriction for values reachable
from the suspended trail. A host opts into either Phase A or Phase B; neither
affects the other's types or behavior.

- `EvaluationCursor.CaptureStructural()` (valid only while `PendingAwait !=
  null`) walks the live suspended `_trail`, each frame's `ScriptEnvironment`
  chain, and any heap objects reachable from them, producing a
  `CursorStructuralCaptureState` — `{ModuleKey, Heap, StaticFields,
  Environments, Trail, RootEnvironmentId, Signal, PendingAwait}`. Every
  reference into this graph is one of:
  - `CapturedHeapValue.Primitive` — an int/float/string/bool/null/array value,
    stored inline (as Phase A's `SerializedValue`).
  - `CapturedHeapValue.AstRef` — a reference to a top-level `ClassValue`/
    `InterfaceValue`/`EnumTypeValue`/free-standing `FunctionValue`, addressed
    by `AstReference` (`{ModuleKey, Path}` — `"module:<identifier>"` or
    `"prelude:<index>"` plus a dotted path like `"Animal"`, `"Animal.speak"`,
    `"Animal.<ctor>"`, `"Color.Red"`, or `"<lambda>@12:8"` for a lambda
    addressed by its `=>` token's position). `RestoreStructural` doesn't
    serialize these declarations — it re-runs each module's declaration
    prefix first (see "decls-before-statements" below), which re-creates
    exactly one instance of each, and `AstResolver` looks it up by address.
  - `CapturedHeapValue.HeapRef` — an index into `CursorStructuralCaptureState.Heap`,
    for `InstanceValue`/`BaseValue` objects. Two-pass capture (assign ids,
    then fill payloads) means cyclic object graphs (`a.next = b; b.next = a;`)
    round-trip with reference identity preserved.
  - `CapturedHeapValue.Method` — a bound method value (`obj.method`): an
    `AstReference` to the method (`"<ClassName>.<methodName>"`) paired with a
    `HeapRef` to the bound instance.
  - `ClassValue.StaticFields` shared mutable state is captured separately as
    `CapturedClassStaticFields` (keyed by the class's `AstReference`) and
    grafted onto the `ClassValue` that the declaration-prefix run re-creates.
  - The cursor's own `PendingAwait` is captured as `CapturedPendingAwait`: for
    a single-element `await`, an `OperationRef` (see "Phase C" below) and
    `ElementType`; for a composite `await [a, b, c]`, a `CapturedAwaitElement`
    per array element — `Resolved` (already-settled value), `Reissue` (a live,
    not-yet-settled operation), or `Fault` (a faulted/replayed-fault element).
  - `CapturedHeapValue.PendingOpRef` — an index into
    `CursorStructuralCaptureState.PendingOperations` (see "Phase C" below).
- `CursorProgramEvaluator.RestoreStructural(graph, state, out result, ...)`:
  (1) runs each module's/prelude's declaration prefix only (collecting the
  resulting module-scope `ScriptEnvironment`s, discarding any trail/
  `PendingAwait`/`Signal` that run produced — declaration prefixes that
  themselves contain top-level statements are covered by the
  decls-before-statements precondition below); (2)
  `EvaluationCursor.RestoreSuspendedState` grafts the captured
  heap/environments/trail/`Signal`/`PendingAwait` on top, resolving
  `CapturedEnvironment.ModuleRef` entries against the environments from step
  1 instead of allocating new ones; (3) for `PendingAwait`, **reissues** every
  `Reissue` operation immediately via `IAsyncOperationBinder.Start` (matching
  `AwaitHandle.ForComposite`'s existing eager `Task.WhenAll` semantics for the
  composite case) — the in-flight operation's own progress is the host's
  concern, not part of this snapshot, so Restore restarts it from scratch.
  Returns `ProgramRunResult.Awaiting` (ready for `Resume`/`ResumeFaulted`), or
  `Completed` if the captured state had no `PendingAwait` (a
  captured-at-completion edge case, mirrors Phase A).
  - Reissued operations are registered with `FunctionValueFactory` via
    `PendingOperationValue.MarkStarted`/`IFunctionValueFactory.RegisterRestored`
    so that, if the restored run suspends again and is never resumed,
    end-of-script `DiscardPending` does not double-start/double-discard them.
- **decls-before-statements precondition**: `CaptureStructural` validates,
  per module/prelude, that every class/interface/enum/function/import/export
  declaration precedes all of that module's top-level statements (top-level
  `var` declarations may freely interleave with statements — they're always
  restored via the captured environment regardless of position). Violating
  this throws `NotSupportedException` at Capture time, with a message
  pointing at this section. This is required because step 1 of
  `RestoreStructural` re-runs the declaration prefix to reconstruct
  `ClassValue`/`FunctionValue`/etc. instances before grafting the captured
  trail — there's no AST-only shortcut to construct these without execution,
  so they must all be establishable before any top-level statement could have
  produced the suspension being restored. See also docs/LANGUAGE_SPEC.md §8.1.
- **Exclusions** (`NotSupportedException` at Capture time): `NativeFunctionValue`/
  `NativeAsyncFunctionValue` reachable from any non-module-scope binding —
  these are thrown by `CapturedHeapValue`'s conversion (module-scope bindings
  of these types are silently skipped, since the declaration-prefix run
  recreates the same `var` initializer regardless). `PendingOperationValue`/
  `ThunkValue` locals are handled by "Phase C" below, with its own narrower
  exclusions.
- `ALKScript.Interpreter.Serialization.CursorStructuralStateSerializer.Capture`/
  `Restore` provide the JSON `byte[]` round trip, mirroring
  `CursorStateSerializer` — `SerializedStructuralCaptureState` and its nested
  DTOs (`SerializedHeapEntry`, `SerializedEnvironment`, `SerializedTrailEntry`,
  `SerializedPendingAwait`, `SerializedAwaitElement`, `SerializedAstReference`,
  `SerializedToken`, `SerializedTypeNode`) convert every type above to/from
  its wire shape, reusing `SerializedValue`/`SerializedOperation` for
  primitives and operation descriptors.

### Addendum 4: `NativeLoopFrame` — design sketch for `map`/`filter` with `await` callbacks (not implemented)

Today, Capture/Restore's `_trail` only contains frames for user-defined
function/method/constructor calls and `await`/`await [...]` suspension points
(see Addendum 3). The native array methods `map` and `filter` are implemented
via `CursorCallInvoker.Call(callback, ...)` — a per-item, native-driven loop
that invokes the user's callback once per array element. If that callback
itself contains an `await` that suspends (e.g. `arr.map(x => await fetch(x))`),
there is currently no trail entry that records "I am partway through a native
`map`/`filter` loop, here is my source array, my current index, and my
accumulated results so far" — so `CaptureStructural()` cannot snapshot this
in-progress native loop, and such a suspension is presently unsupported.

A future session could close this gap by adding a new `TrailEntry` variant,
sketched as:

```
TrailEntry.NativeLoopFrame {
  MethodName: "map" | "filter",
  SourceArray: HeapRef,          // the receiver array being iterated
  CurrentIndex: int,              // index of the element whose callback is in flight
  Accumulated: IReadOnlyList<CapturedHeapValue>, // results collected so far
  Callback: CapturedHeapValue,    // Method / AstRef / NativeMethod (Item 2) — NOT a lambda
}
```

On `RestoreStructural`, a `NativeLoopFrame` would be grafted back onto the
trail, and `CursorCallInvoker`'s `map`/`filter` driver would resume the loop
from `CurrentIndex`, re-invoking `Callback` for the in-flight element (the
suspended callback invocation itself is represented by the trail frames above
the `NativeLoopFrame`, exactly as for any other suspended call) and continuing
to populate `Accumulated` for subsequent elements.

Constraints/notes for that future work:

- **Callback must be capturable.** `Callback` reuses the `Method`/`AstRef`/
  `NativeMethod` (Item 2) representations introduced for Capture/Restore of
  function-valued locals. A `map`/`filter` callback that is itself a lambda
  closing over local state remains excluded, consistent with the existing
  `CaptureStructural_LocalBoundToLambdaValue_Throws` exclusion.
- **Receiver/argument idempotency.** As with `CursorCallInvoker.Construct`'s
  existing precedent for `new`, the receiver array expression (and any
  arguments to `map`/`filter` itself) must be safe to re-evaluate if the
  enclosing statement is re-executed on resume — see the
  "decls-before-statements"/re-execution caveat in docs/LANGUAGE_SPEC.md §8.1.
- **Strict superset of Item 4 Option B.** The deferred "Option B" approach
  (a general expression-level resume trail, allowing `await` in arbitrary
  expression positions by checkpointing partially-evaluated expressions) would
  need exactly this kind of "partial progress through a multi-step, native-
  driven expression evaluation" machinery — but generalized to *any* expression,
  not just `map`/`filter`. If Option B is ever pursued, `NativeLoopFrame` should
  be designed as a special case of it (or implemented first, as the simpler,
  narrower case that validates the general approach).
**Capture/Restore ("Phase C", pending operations in locals)**: closes Phase
B's gap where any `PendingOperationValue`/`ThunkValue` reachable from a local
variable other than the suspending await's own operand threw
`NotSupportedException` — the game-dev pattern `var op = startLongTask(); ...
await op;` (across many ticks, possibly Captured/Restored before the `await`
is ever reached).

- `CursorStructuralCaptureState.PendingOperations` is a new top-level table
  (`List<CapturedPendingOperation>`), analogous to `Heap`. Every
  `PendingOperationValue`/`ThunkValue` reachable from a local — and the
  cursor's own awaited operand, via `CapturedPendingAwait.OperationRef` — is
  captured here, two-pass-deduplicated by reference identity exactly like
  `Heap`/`GetHeapId` (`pendingOpIds`/`GetPendingOpId`). The *same* underlying
  instance referenced from both a local `op` and the suspending `await op`
  resolves to a single shared table entry, so `Restore` reconstructs/reissues
  it exactly once — `IAsyncOperationBinder.Start` is called once, not twice,
  and `op`/the reissued await operand observably refer to the same
  reconstructed operation.
- Each `CapturedPendingOperation` is `{Element: CapturedAwaitElement,
  WasStarted: bool}`, reusing Phase B's `CapturedAwaitElement` union:
  - `Resolved` — a `ThunkValue` whose task already `RanToCompletion`, or a
    `PendingOperationValue` whose started task has completed successfully.
    Reconstructed via `ThunkValue.FromResult`.
  - `Fault` — a `ThunkValue`/`PendingOperationValue` whose task is `Faulted`.
    Reconstructed as a `ThunkValue` wrapping `Task.FromException`.
  - `Reissue` — a `PendingOperationValue` with a recoverable
    `PendingOperation`. `WasStarted` records whether `Start()` had already
    been called at capture time (even if not yet settled):
    - `WasStarted = false` (e.g. `var op = startLongTask();` before any
      `await op`) — `Restore` constructs a fresh, **not-started**
      `PendingOperationValue`; the script's own later `await op` triggers the
      actual `IAsyncOperationBinder.Start` call.
    - `WasStarted = true` — `Restore` eagerly calls
      `IAsyncOperationBinder.Start` during reconstruction (mirroring Phase
      B's existing `Reissue` handling for the cursor's own operand), and
      registers the result with `FunctionValueFactory.RegisterRestored` so
      end-of-script `DiscardPending` doesn't double-discard it.
- **Composite-element aliasing**: a composite `await [a, b, c]` element whose
  underlying `PendingOperationValue`/`ThunkValue` instance is *also*
  referenced from a local (e.g. `var op = fetch(); var r = await [op, 5];`) is
  captured as `CapturedAwaitElement.OperationRef(id, elementType)` — the
  element's `AwaitElement.Source` is looked up in `pendingOpIds` (already
  populated by the local's own `GetPendingOpId` call, since environments are
  captured before `PendingAwait`) and, if present, the element shares that
  `PendingOperations` table entry instead of capturing a second, independent
  `Reissue`. A non-aliased composite element is captured exactly as before
  (`Resolved`/`Reissue`/`Fault`, independent of the table). On `Restore`,
  `OperationRef` elements are rebuilt from the already-reconstructed
  `PendingOperations[id]` value — a `PendingOperationValue` (guaranteed
  started, since composite elements are always eagerly started) contributes
  its `StartedTask`, a `ThunkValue` contributes its `Task` — via
  `AwaitElement.ForTask`, so `IAsyncOperationBinder.Start` is called exactly
  once and `op`/the composite element observably refer to the same
  reconstructed operation.
- **Exclusions** (`NotSupportedException` at Capture time):
  - A still-*pending* `ThunkValue` with no backing `PendingOperationValue` —
    fundamental, not just unimplemented: `ThunkValue` carries no
    `PendingOperation` descriptor, so there is nothing for `Restore` to
    reissue.
- Wire format: `SerializedStructuralCaptureState.PendingOperations` (a list of
  `SerializedPendingOperation { Element: SerializedAwaitElement, WasStarted:
  bool }`), `SerializedHeapValue`'s `"pendingopref"` kind,
  `SerializedAwaitElement`'s `"operationref"` kind, and
  `SerializedPendingAwait.OperationRef`.

- Native array-method callbacks (`map`/`filter`, etc.) that themselves `await`
  are not supported — these run via `cursor.Call(...)` and a callback
  returning `IsAwaiting` is unhandled.

**Cutover complete**: `CursorProgramEvaluator`/`EvaluationCursor` is now the
sole evaluator. The original `Task`-based evaluator (`ProgramEvaluator` +
`ScriptScheduler`/`ScheduledTask`, plus `ExpressionEvaluator`/
`StatementExecutor`/`CallInvoker` and their interfaces) has been deleted,
along with the differential test suite that compared the two
(`CursorDifferentialTests`/`CursorEvaluatorTestBase`).

`IProgramRuntime`/`ProgramRuntime` (`ALKScript.Interpreter.Runtime`) now expose
`CursorProgramEvaluator`'s suspend/resume model directly via `ProgramRun`:
`RunFromGraph`/`RunFromSource`/`RunFromFile` return a `ProgramRun`, which
exposes `Result` (`ProgramRunResult`), `PendingAwait`, `Resume`,
`ResumeFaulted`, and `RunToCompletion(IAsyncOperationBinder?)`. The old
`Pump()`/`RunUntilComplete(ScriptEvaluation)`/`IScriptLoop`/`ScriptEvaluation`
surface has been removed.

### Addendum 5: removing `System.Threading.Tasks.Task` from the suspension model

Addendum 3 kept `Task` as "purely a host-boundary type": `IAsyncOperationBinder.Start`
returned `Task<ALKScriptValue>`, `PendingOperationValue.Start()` returned that
same `Task`, `ThunkValue` could wrap a still-running `Task`, and `AwaitHandle`/
`AwaitElement` exposed `Task`/`CompositeTask` (a `Task.WhenAll`) for the host to
await. In practice this meant a host whose async operation couldn't settle
synchronously (e.g. a real HTTP call) had to hand the evaluator a live `Task`
and the evaluator's `RunToCompletion` would block on `GetAwaiter().GetResult()`
— i.e. the evaluator still owned a slice of the host's async machinery.

This addendum removes `Task` from every type that crosses the evaluator/host
boundary, and from the evaluator's own internals. `Task` may still exist
*inside* a host's `IAsyncOperationBinder` implementation (or in test helpers)
— it just never appears in `PendingOperationValue`, `ThunkValue`, `AwaitElement`,
`AwaitHandle`, or `IAsyncOperationBinder`'s signatures.

**New type — `OperationStatus`** (`ALKScript.Interpreter.Common/Evaluation/Scheduling/OperationStatus.cs`):
a Task-free tri-state — `Pending` (singleton), `Resolved(ALKScriptValue Value)`,
or `Faulted(Exception Error)` — replacing `Task<ALKScriptValue>` as the result
of starting or polling an operation.

**`IAsyncOperationBinder` redesign**: `Start(PendingOperation)` now returns
`OperationStatus` directly instead of `Task<ALKScriptValue>`. If the host's
effect can't settle synchronously, `Start` queues it and returns
`OperationStatus.Pending` immediately — the evaluator suspends right there,
with no `Task`, no thread, no `GetAwaiter().GetResult()`. A new method,
`Poll(PendingOperation)`, is called by the host's "pump" for any operation
whose last-known status was `Pending`, and returns its current status. The
existing `Discard`/`OnOperationFaulted` members are unchanged.

**`PendingOperationValue` redesign**: `_started: Task<ALKScriptValue>?` becomes
`_status: OperationStatus?`. `Start()` is still memoized
(`_status ??= _binder.Start(Operation)`); a new `Poll()` re-polls the binder
only while `_status is OperationStatus.Pending`, and is a no-op (returns the
cached status, or `Pending` if `Start` was never called) otherwise. The
internal `Status` property exposes the cached `OperationStatus?` for
inspection (by Capture and by `AwaitElement.NeedsSuspend`) without triggering
`Start`. `MarkStarted(Task)` is gone — Capture/Restore call `Start()` directly
(idempotent via memoization), and a small internal `MarkSettled(OperationStatus)`
lets Restore reconstruct an already-faulted operation without going through
the binder at all (see below).

**`ThunkValue` simplification**: a `ThunkValue` is now *always* an
already-settled `{ ALKScriptValue Result, TypeNode? ElementType }` — the
"wrap a live/faulted `Task` directly" role is gone entirely. Any native
operation that can genuinely be pending must be declared `native async` and go
through `PendingOperation`/`IAsyncOperationBinder` — the single path for
pending state. A synchronous native binding that needs a `Task` internally
(e.g. it calls another async API) must block on it
(`.GetAwaiter().GetResult()`) before wrapping the resulting value in
`ThunkValue` — `Task` stays inside that host binding, never crossing into
`ThunkValue` itself.

**`AwaitElement`/`AwaitHandle` redesign** (`ALKScript.Interpreter.Evaluator/Cursor/AwaitHandle.cs`):
`Task`/`CompositeTask`/`ForTask`/`ForPendingTask` are gone.
`AwaitElement` now carries `Resolved`/`Operation`/`ElementType`/
`ReplayedFaultMessage`/`Source` (the underlying `PendingOperationValue` or
`ThunkValue`, for `ResolveWhenAll` to re-inspect) and a `NeedsSuspend` flag —
true only while `Source` is a `PendingOperationValue` whose current `Status`
is `null` or `Pending`. `AwaitHandle.ForComposite` no longer builds a
`Task.WhenAll`; it just stores the element list, and whether the composite
handle represents a real suspension falls out of whether any element's
`NeedsSuspend` is true.

**`ProgramRun.Pump()`** (`ALKScript.Interpreter.Evaluator/Cursor/ProgramRun.cs`)
is the new host-facing "tick" primitive, replacing the old block-on-`Task`
approach:

```csharp
public ProgramRunResult Pump()
{
    if (Result != ProgramRunResult.Awaiting) return Result;
    var handle = PendingAwait!;

    if (handle.CompositeElements != null)
    {
        var allSettled = true;
        foreach (var element in handle.CompositeElements)
            if (element.Source is PendingOperationValue pending && pending.Poll() is OperationStatus.Pending)
                allSettled = false;
        if (allSettled) Resume(NullValue.Instance);
        return Result;
    }

    var pendingOperation = (PendingOperationValue)handle.Source!;
    switch (pendingOperation.Poll())
    {
        case OperationStatus.Resolved resolved: Resume(resolved.Value); break;
        case OperationStatus.Faulted faulted: ResumeFaulted(faulted.Error.Message); break;
        // Pending: leave Result == Awaiting.
    }
    return Result;
}
```

`Pump()` is a no-op (returns `Result` unchanged) unless the run is currently
`Awaiting`, and polls each still-pending operation via
`IAsyncOperationBinder.Poll` at most once per call — safe to call repeatedly
from a host's own loop (game tick, event loop, etc.). `RunToCompletion()` is
now just `while (Result == Awaiting) { if (Pump() == Awaiting) Thread.Sleep(1); }`
— suitable for tests and binders whose `Start`/`Poll` never return `Pending`
(they block internally), but for genuinely-async binders this busy-polls; such
hosts should drive `Pump()` from their own loop instead. Both methods dropped
the `IAsyncOperationBinder? binder` parameter — each `PendingOperationValue`
already owns its binder from construction.

**Capture/Restore**: Capture inspects `PendingOperationValue.Status`
(`null`/`Pending` → `CapturedAwaitElement.Reissue`; `Resolved`/`Faulted` →
`Resolved`/`Fault`, same as before) instead of inspecting a `Task`'s status.
Since `ThunkValue` is now always-resolved, the old "cannot capture a
still-pending thunk" `NotSupportedException` branch is unreachable and was
removed. On Restore, a captured `Resolved`/`Fault` element reconstructs as a
plain `ThunkValue(value, elementType)` (resolved) or — since `ThunkValue` can
no longer represent a fault — as an already-settled, already-replayed
`PendingOperationValue` whose `Status` is set directly via the internal
`MarkSettled(OperationStatus.Faulted(...))`, without calling the binder.
`Reissue` elements still reconstruct via `Start()` as before.

**Net effect**: the only place `Task` can appear anywhere in this picture is
inside a host's own `IAsyncOperationBinder.Start`/`Poll` implementations (e.g.
blocking on an internal `Task` before returning `Resolved`, or stashing a
`Task`-backed operation in a side table that `Poll` checks) — exactly the
"host manages its own queue of truly-async operations, outside the scope of
the evaluator" model this addendum was written to enable.
