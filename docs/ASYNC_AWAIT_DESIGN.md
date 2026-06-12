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

  **Deferred follow-ups** (not implemented):
  - **Phase B** ("structural snapshot"): O(1) restore by serializing the live
    `_trail`/environment/heap graph directly via AST-node references
    (`(moduleIdentifier, declarationName)`, `(classRef, memberName)`, etc.)
    to reconstruct `InstanceValue`/`ClassValue`/`FunctionValue`/closures
    without re-running anything — and would lift Phase A's primitive-value
    restriction. Only worth pursuing if Phase A's O(total-log) replay cost
    proves insufficient in practice.
  - **Phase C**: reconstructing mid-flight `PendingOperationValue`/`ThunkValue`
    held in *other* local variables (not the suspending await's own operand)
    as fresh not-yet-started operations on `Restore`.
- Native array-method callbacks (`map`/`filter`, etc.) that themselves `await`
  are not supported — these run via `cursor.Call(...)` and a callback
  returning `IsAwaiting` is unhandled.

`CursorProgramEvaluator`/`EvaluationCursor` exist alongside the original
`Task`-based evaluator (`ProgramEvaluator` + `ScriptScheduler`) — differential
testing (`Tests.ALKScript.Interpreter.Evaluator/Cursor/CursorDifferentialTests.cs`)
confirms both produce identical `record()` output for the in-scope subset of
the existing test suite's scripts. Cutover (making `CursorProgramEvaluator`
the only evaluator and deleting `ScriptScheduler`/`ScheduledTask`/etc.) is a
later plan, once the limitations above are addressed.
