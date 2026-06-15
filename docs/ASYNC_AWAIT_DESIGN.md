# Async/Await Architecture

This document describes the design of ALKScript's `await`/`thunk` system and
the evaluator architecture that implements it, as currently implemented. It
covers the host-facing contracts (`IAsyncOperationBinder`, `ProgramRun`,
`OperationStatus`), the evaluator's suspend/resume mechanism
(`EvaluationCursor`), and the save/load (Capture/Restore) model. For the
language-level syntax and semantics of `thunk`/`thunk<T>` and `await`, see
[LANGUAGE_SPEC.md](LANGUAGE_SPEC.md) §8.

---

## 1. Goals and constraints

The motivating scenario is **single-threaded game scripting**:

- Calling a `native async` operation (e.g. "move object to point") starts a
  real game-side effect that may span many game-loop ticks/frames.
- `await` suspends the script until that operation completes — the script and
  the game loop never run concurrently (strict turn-taking).
- A `native async` operation does **not** begin running merely because it was
  called — only when it is `await`ed, or the script finishes execution
  entirely (lazy/deferred start). The latter is what makes
  **fire-and-forget** possible: a script calls `moveTo(npc, x, y)` without
  awaiting it, does other things, and ends; reaching the end of the script is
  the signal that converts the un-awaited call into a fire-and-forget start
  (the `Discard` mechanism, §4).
- Cancelling a running script also cancels any in-flight operations it
  started.
- Many scripts may be running/awaiting concurrently — thread-per-script is
  unacceptable.
- Suspended script state must be serializable, so a game can pause, save,
  reload, and resume scripts that were mid-`await`.

The architecture below satisfies all of these without using
`System.Threading.Tasks.Task` anywhere in the evaluator or in the
evaluator/host boundary — `Task` may exist *inside* a host's own
`IAsyncOperationBinder` implementation, but never crosses into ALKScript's
own types.

---

## 2. Language-level model (summary)

ALKScript scripts are single-threaded and cannot create a deferred operation
themselves — the only source of one is a `native` declaration provided by the
embedding host (`PendingOperationValue`/`ThunkValue`, both reporting
`TypeName == "thunk"`). Script code can hold, forward, or `await` such a
value, but never constructs one directly.

- `thunk`/`thunk<T>` is a real, writable, type-erased type usable anywhere a
  type is expected. Only `native` declarations may declare a
  `thunk`/`thunk<T>` return type.
- `await expr` suspends until the operation settles and yields its result
  (unwrapping `thunk<T>` to `T`). `await` is a universally-valid prefix
  operator, usable in the body of any function/method/lambda, including the
  entry module's top level, with no declaration-level marker.
- `await` on a non-`thunk` value is a lenient no-op (yields the value
  unchanged).
- `await [a, b, ...]` is sugar for awaiting all elements concurrently
  (`whenAll` semantics): the result is an array of each element's result, and
  any fault is aggregated and surfaces as a single catchable error.
- A `thunk`/`thunk<T>`-returning `native` called without `await` is not
  started eagerly; if the script ends without awaiting it, the host's binder
  receives a `Discard` call so the operation can run as a fire-and-forget
  effect.

`await` (including the `await [...]` form) is restricted to a small set of
syntactic positions, enforced at parse time — see LANGUAGE_SPEC.md §8.1 for
the exact list and the rationale (these positions are exactly the ones the
evaluator's resume trail, §3, can checkpoint and restart cleanly).

---

## 3. Evaluator architecture: `EvaluationCursor`

ALKScript is single-threaded and never performs real concurrency itself —
suspension exists purely to give `await` its suspend/resume semantics. Rather
than relying on the CLR's compiler-generated `async`/`await` state machines
(which are opaque and not serializable — a hard blocker for save/load, §6),
the evaluator is a **hand-rolled, synchronous, resumable traverser**:
`EvaluationCursor` (`ALKScript.Interpreter.Evaluator/Cursor/`).

- Every evaluator method returns `StepResult` (`IsAwaiting` / `Value` /
  `Handle`) instead of `Task<ALKScriptValue>`/`Task`. The propagation pattern
  at each suspension point is:
  ```csharp
  var step = Eval(expr, env);
  if (step.IsAwaiting) return step;   // propagate suspension upward
  var x = step.Value!;
  ```
- `Signal` (break/continue/return/thrown/cancelled) is the existing,
  orthogonal non-local-exit mechanism, checked after each sub-step exactly as
  before suspension support was added. `ExecuteTry` threads any pending
  signal through `finally` unconditionally and only special-cases `Thrown` for
  `catch`, so `Cancelled` automatically bypasses `catch` while still running
  `finally`.
- When an `await` expression is reached on a not-yet-resolved
  `thunk`/`thunk<T>`/`PendingOperationValue` in an allowed position (per
  LANGUAGE_SPEC.md §8.1), `Eval` returns `StepResult.Awaiting(handle)` and
  `EvaluationCursor.Run()` returns `RunResult.Awaiting` — synchronously, with
  no `Task`, no scheduler, no thread involved. The host later calls
  `Resume(value)`/`ResumeFaulted(message)` once the operation settles (see
  §4).

### Resume trail

When a run suspends, every enclosing resumable construct (block/loop/if/
switch/try) records, leaf-to-root, which child (statement/iteration/case/
region index) it was executing and with which `ScriptEnvironment` — the
**resume trail**. `Resume` walks this trail root-to-leaf to fast-forward back
to the exact suspended statement, re-entering only the chosen
branches/iterations/regions without re-evaluating anything already evaluated,
then substitutes the resumed value for the `await` that suspended and
continues normal execution.

`CursorProgramEvaluator` is the module-graph counterpart to a single-cursor
run: it runs the global prelude(s) then each module's top-level declarations
as separate `EvaluationCursor.Start` "segments", preserving topological module
ordering, import/export binding, and the record-and-replay log (§6.1).

### Current limitations

- Function, method, and constructor bodies (including `base(...)`
  super-constructor recursion) may suspend mid-body — the resume trail is a
  single flat list spanning all nested `ExecuteBlock` calls regardless of C#
  call-stack depth; a suspending nested call simply propagates `IsAwaiting` up
  through `CursorCallInvoker`.
- A suspended constructor's `new` expression is itself re-evaluated on resume
  (per the next point), which would normally allocate a second, distinct
  instance. `CursorCallInvoker.Construct` special-cases this: while resuming,
  it recovers the original instance from the resume trail's captured `this`
  (via `PeekResumeEnvironment`) and reuses it, so field mutations performed by
  the resumed constructor body land on the instance the `new` expression
  ultimately returns.
- Field/static-field initializers cannot suspend — `InitializeFields` runs
  outside the resume trail, and `CursorCallInvoker` throws if an initializer
  expression returns `IsAwaiting`. This is also enforced at parse time (no
  `await` in initializer expressions at all).
- When a statement containing a suspending call is re-executed on resume,
  `EvalCall`/`EvalNew` re-evaluate the callee and all argument expressions
  from scratch — there is no short-circuit for call arguments as there is for
  `AwaitExpr`'s operand. Argument expressions with side effects therefore run
  twice across a suspend/resume cycle. Worse, `IsResuming` stays true for the
  whole re-executed statement (until the trail is fully unwound), so if a
  re-evaluated argument expression itself makes a call, that call's body also
  sees `IsResuming == true` and consumes the resume-trail entry meant for the
  original suspended call — e.g. for `var y = withAwait(sideEffect())` where
  `withAwait` is the one that suspended, on resume `sideEffect()` is evaluated
  first (as the argument), its body unwinds the trail entry meant for
  `withAwait`'s body, and `withAwait` is then entered *fresh* with the newly
  re-evaluated argument. **Mitigation**: bind such expressions to a local
  first, e.g. `var a = sideEffect(); var y = foo(a);`.
- Native array-method callbacks (`map`/`filter`) that themselves `await` are
  not supported — see §7.

---

## 4. Host integration

### `OperationStatus`

A `Task`-free tri-state (`ALKScript.Interpreter.Common/Evaluation/Scheduling/OperationStatus.cs`)
representing the result of starting or polling an operation:

- `Pending` (singleton) — not yet settled.
- `Resolved(ALKScriptValue Value)` — settled successfully.
- `Faulted(Exception Error)` — settled with an error.

### `IAsyncOperationBinder`

The host implements this interface and assigns it to
`ProgramRuntime.OperationBinder` (or passes it to `ProgramRun.Pump`/
`RunToCompletion`) before running any program containing `native async`
declarations:

```csharp
public interface IAsyncOperationBinder
{
    OperationStatus Start(PendingOperation operation);
    OperationStatus Poll(PendingOperation operation);
    void Discard(PendingOperation operation, Action<ALKScriptValue> onUnobservedFault);
    void OnOperationFaulted(PendingOperation operation, Exception fault);
}
```

- `Start` is called the first time an operation is awaited (or, for
  fire-and-forget, when the script ends with the operation never awaited). If
  the host's effect can't settle synchronously, `Start` queues it and returns
  `OperationStatus.Pending` immediately — the evaluator suspends right there,
  with no `Task`, no thread, no blocking wait.
- `Poll` is called by the host's "pump" (`ProgramRun.Pump`, below) for any
  operation whose last-known status was `Pending`, and returns its current
  status.
- `Discard` handles an operation that was never awaited and is being converted
  to fire-and-forget at end-of-script. `onUnobservedFault` lets the host learn
  about a fault that occurs *after* the script context no longer exists.
- `OnOperationFaulted` is a complementary hook, in the same family as
  `onUnobservedFault` but on a different schedule: every individual fault
  (including each member of a `whenAll`) is reported here for
  logging/telemetry, *in addition to* the aggregate fault the script itself
  can catch (§5).

`PendingOperationValue` memoizes the binder call: `Start()` caches the
returned `OperationStatus` (`_status ??= _binder.Start(Operation)`), and
`Poll()` re-polls the binder only while `_status is OperationStatus.Pending`
(otherwise it's a no-op returning the cached status). The internal `Status`
property exposes the cached value for inspection by Capture and by
`AwaitElement.NeedsSuspend` without triggering `Start`.

A `ThunkValue` is always an *already-settled* `{ ALKScriptValue Result,
TypeNode? ElementType }` — it cannot represent a pending or faulted operation.
Any native operation that can genuinely be pending must be declared `native
async` and go through `PendingOperation`/`IAsyncOperationBinder`. A
synchronous native binding that needs a `Task` internally (e.g. it calls
another async API) must block on it (`.GetAwaiter().GetResult()`) before
wrapping the result in `ThunkValue` — `Task` stays inside that host binding.

### `ProgramRun`

`IProgramRuntime`/`ProgramRuntime` (`ALKScript.Interpreter.Runtime`) expose
`CursorProgramEvaluator`'s suspend/resume model via `ProgramRun`:
`RunFromGraph`/`RunFromSource`/`RunFromFile` return a `ProgramRun`, which
exposes:

- `Result` (`ProgramRunResult`: `Completed` / `Awaiting` / `Faulted`)
- `PendingAwait` (the current `AwaitHandle`, if `Awaiting`)
- `Resume(value)` / `ResumeFaulted(message)`
- `Pump()` — the host-facing "tick" primitive
- `RunToCompletion(IAsyncOperationBinder?)`

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
`while (Result == Awaiting) { if (Pump() == Awaiting) Thread.Sleep(1); }` —
suitable for tests and binders whose `Start`/`Poll` never return `Pending`
(they block internally); genuinely-async binders should drive `Pump()` from
their own loop instead.

`AwaitElement`/`AwaitHandle` (`ALKScript.Interpreter.Evaluator/Cursor/AwaitHandle.cs`)
carry `Resolved`/`Operation`/`ElementType`/`ReplayedFaultMessage`/`Source` (the
underlying `PendingOperationValue` or `ThunkValue`) and a `NeedsSuspend` flag
— true only while `Source` is a `PendingOperationValue` whose current `Status`
is `null` or `Pending`. `AwaitHandle.ForComposite` stores the element list for
a `whenAll`; whether it represents a real suspension falls out of whether any
element's `NeedsSuspend` is true.

### `pause(frames)`

A `pause(frames)` primitive lets scripts that would otherwise starve the frame
yield voluntarily. It is a **standard-library `native async` function**
(`async native pause(int frames)`), not a language keyword — this preserves
"`await` is the one true suspension primitive", avoids grammar bloat, and
avoids a naming collision with generator-style "yield" in other languages.

The implementation is entirely the **host's responsibility**, inside its
`IAsyncOperationBinder.Start` like any other async native operation. The
guarantee: after `pause(1)`, the script resumes seeing game state as of the
next completed simulation step (generalizes to `pause(n)` for `n` steps). A
host implementing this counts how many more `Pump()` calls should elapse
before settling the operation, since the host controls when `Pump()` is
called and therefore knows exactly when each frame ends. No `AdvanceFrame()`
or other frame-awareness API is needed in the scheduler.

---

## 5. Scheduling, ordering, and faults

- **Script identity**: one compiled program (`ModuleGraph`), run by many
  concurrent, independent instances (`ProgramRun`s). Compilation is shared;
  each running instance has its own suspended state.
- **Scheduling order**: deterministic and stable (insertion/registration
  order), never hash-based iteration. Guaranteeing this from the start is
  cheap; retrofitting it later, once content depends on incidental ordering,
  would be expensive and likely breaking.
- **`whenAll` policy** (`await [a, b, ...]`):
  - Siblings are **not** cancelled when one member faults — every member runs
    to completion regardless of how others resolve.
  - All faults are aggregated into a single catchable error surfaced from the
    `await` (mirroring `Task.WhenAll` + `AggregateException`).
  - Every individual fault is *also* reported to the host via
    `OnOperationFaulted`, in addition to the aggregate the script can catch.
  - Together these guarantee `whenAll` cannot resolve until every member has
    settled, which means there is no "script already moved on, then a sibling
    faults into the void" case — every fault is either caught by the script
    (via the aggregate) or reported to the host (`OnOperationFaulted`/
    `Discard`'s `onUnobservedFault`), never both lost.
- **Race combinators** (`whenAny`, "first one wins"): not supported, and not
  planned — incompatible with the run-to-completion guarantee above. There is
  no definition of `whenAny` that resolves on the first settlement while
  still guaranteeing every member runs to completion.
- **Cancellation**: a shared `CancellationToken` reaches every member
  operation of a `whenAll`. The combinator's own suspension point is itself an
  ordinary, cancellation-aware suspension (subject to `Signal.Cancelled`
  exactly like any other `await`) — not something that merely waits for member
  operations to fault their way to a resolution.
- **Combinators beyond `await [...]`**: any further `Task`-like API surface
  belongs in the standard library, not the language grammar — the same
  category as `Array`, `Error`, `HttpClient`, which are already `native`-backed
  stdlib rather than built-in syntax.

---

## 6. Save and load

Save/load of suspended scripts is architecture-defining: compiler-generated
`async`/`Task` continuations would be opaque and version-dependent, which is
exactly why `EvaluationCursor` (§3) exists instead. Two complementary, additive
Capture/Restore mechanisms exist; a host opts into either:

### 6.1 Record-and-replay (operation log)

A flat, ordered log of `(operation, result-or-fault)` pairs per script
instance, à la Temporal / Azure Durable Functions:

- `EvaluationCursor.Capture()` (valid only while `PendingAwait != null`) and
  `CursorProgramEvaluator.Capture()`/`Restore()` snapshot/reconstruct a
  suspended run by reusing this log almost entirely as-is. `Capture()` returns
  a `CursorCaptureState { Phase, ModuleIndex, Log }` — the operation log
  recorded so far via `RecordEntry`.
- `Restore(graph, ..., state, out result)` builds a fresh
  `CursorProgramEvaluator` seeded with `state.Log` as its replay log and calls
  `Evaluate(graph)` from scratch: every `await`/`whenAll` site with a
  corresponding replay-log entry resolves instantly via `TryReplayNext()`
  until the log is exhausted, at which point the run suspends again at the
  same logical point (`ProgramRunResult.Awaiting`, ready for
  `Resume`/`ResumeFaulted`) — or, if the captured state was the run's final
  suspension, completes (`ProgramRunResult.Completed`).
- The caller must supply an equivalent `ModuleGraph` (rebuilt from the same
  source files/module identifiers); this mechanism does not serialize the
  AST/module graph itself.
- This requires two supporting properties of the runtime:
  - **Deterministic script logic between `await`s** — replay must reproduce
    the same sequence of operations given the same log (§6.4 below scopes
    exactly what must be logged for this to hold).
  - **Standard library design surface** — combinators belong in the standard
    library (§5), keeping the deterministic/replay-relevant surface small and
    centrally controlled.

`OperationLogEntry`/`ALKScriptValue` DTOs carry no serialization-format
dependency. `ALKScript.Interpreter.Serialization` owns converting them to/from
a wire format: `OperationLogEntrySerializer`/`SerializedValue` (JSON via
`System.Text.Json`) and the public `CursorStateSerializer.Capture`/`Restore`
(`byte[]` round trip). `SerializedValue` restricts values that ever cross the
log boundary (an `await`'s recorded result) to
`IntValue`/`FloatValue`/`StringValue`/`BoolValue`/`NullValue`/`ArrayValue` of
those (recursively) — any other runtime value (`InstanceValue`/
`FunctionValue`/`ThunkValue`/`PendingOperationValue`/`ClassValue`/etc.) throws
`NotSupportedException` at serialize time. A host that needs a different wire
format, or to lift this restriction, can call
`CursorProgramEvaluator.Capture`/`Restore` directly and write its own
serializer against `CursorCaptureState`/`OperationLogEntry`.

### 6.2 Structural snapshot

A second, additive Capture/Restore pair giving O(1) restore independent of a
run's history length, and lifting §6.1's primitive-only restriction for values
reachable from the suspended trail.

- `EvaluationCursor.CaptureStructural()` (valid only while `PendingAwait !=
  null`) walks the live suspended `_trail`, each frame's `ScriptEnvironment`
  chain, and any heap objects reachable from them, producing a
  `CursorStructuralCaptureState { ModuleKey, Heap, StaticFields,
  Environments, Trail, RootEnvironmentId, Signal, PendingAwait }`. Every
  reference into this graph is one of:
  - `CapturedHeapValue.Primitive` — an int/float/string/bool/null/array value,
    stored inline (as §6.1's `SerializedValue`).
  - `CapturedHeapValue.AstRef` — a reference to a top-level `ClassValue`/
    `InterfaceValue`/`EnumTypeValue`/free-standing `FunctionValue`, addressed
    by `AstReference` (`{ModuleKey, Path}` — `"module:<identifier>"` or
    `"prelude:<index>"` plus a dotted path like `"Animal"`, `"Animal.speak"`,
    `"Animal.<ctor>"`, `"Color.Red"`, or `"<lambda>@12:8"` for a lambda
    addressed by its `=>` token's position). `RestoreStructural` doesn't
    serialize these declarations — it re-runs each module's declaration prefix
    first (decls-before-statements, below), which re-creates exactly one
    instance of each, and `AstResolver` looks it up by address.
  - `CapturedHeapValue.HeapRef` — an index into
    `CursorStructuralCaptureState.Heap`, for `InstanceValue`/`BaseValue`
    objects. Two-pass capture (assign ids, then fill payloads) means cyclic
    object graphs (`a.next = b; b.next = a;`) round-trip with reference
    identity preserved.
  - `CapturedHeapValue.Method` — a bound method value (`obj.method`): an
    `AstReference` to the method (`"<ClassName>.<methodName>"`) paired with a
    `HeapRef` to the bound instance.
  - `CapturedHeapValue.PendingOpRef` — an index into
    `CursorStructuralCaptureState.PendingOperations` (§6.3).
  - `ClassValue.StaticFields` shared mutable state is captured separately as
    `CapturedClassStaticFields` (keyed by the class's `AstReference`) and
    grafted onto the `ClassValue` that the declaration-prefix run re-creates.
  - The cursor's own `PendingAwait` is captured as `CapturedPendingAwait`: for
    a single-element `await`, an `OperationRef` (§6.3) and `ElementType`; for a
    composite `await [a, b, c]`, a `CapturedAwaitElement` per array element —
    `Resolved` (already-settled value), `Reissue` (a live, not-yet-settled
    operation), or `Fault` (a faulted/replayed-fault element).

- `CursorProgramEvaluator.RestoreStructural(graph, state, out result, ...)`:
  1. Runs each module's/prelude's declaration prefix only (collecting the
     resulting module-scope `ScriptEnvironment`s, discarding any
     trail/`PendingAwait`/`Signal` that run produced).
  2. `EvaluationCursor.RestoreSuspendedState` grafts the captured
     heap/environments/trail/`Signal`/`PendingAwait` on top, resolving
     `CapturedEnvironment.ModuleRef` entries against the environments from
     step 1 instead of allocating new ones.
  3. For `PendingAwait`, **reissues** every `Reissue` operation immediately via
     `IAsyncOperationBinder.Start` (matching `AwaitHandle.ForComposite`'s
     eager semantics for the composite case) — the in-flight operation's own
     progress is the host's concern, not part of this snapshot, so Restore
     restarts it from scratch.

  Returns `ProgramRunResult.Awaiting` (ready for `Resume`/`ResumeFaulted`), or
  `Completed` if the captured state had no `PendingAwait` (a
  captured-at-completion edge case, mirroring §6.1).

  Reissued operations are registered with `FunctionValueFactory` via
  `PendingOperationValue.MarkStarted`/`IFunctionValueFactory.RegisterRestored`
  so that, if the restored run suspends again and is never resumed,
  end-of-script `DiscardPending` does not double-start/double-discard them.

#### decls-before-statements precondition

`CaptureStructural` validates, per module/prelude, that every
class/interface/enum/function/import/export declaration precedes all of that
module's top-level statements (top-level `var` declarations may freely
interleave with statements — they're always restored via the captured
environment regardless of position). Violating this throws
`NotSupportedException` at Capture time, with a message pointing at this
section. This is required because step 1 of `RestoreStructural` re-runs the
declaration prefix to reconstruct `ClassValue`/`FunctionValue`/etc. instances
before grafting the captured trail — there's no AST-only shortcut to construct
these without execution, so they must all be establishable before any
top-level statement could have produced the suspension being restored. See
also LANGUAGE_SPEC.md §8.1.

#### Exclusions (`NotSupportedException` at Capture time)

- `NativeFunctionValue`/`NativeAsyncFunctionValue` reachable from any
  non-module-scope binding — module-scope bindings of these types are
  silently skipped, since the declaration-prefix run recreates the same `var`
  initializer regardless.
- `PendingOperationValue`/`ThunkValue` locals are handled by §6.3, with its own
  narrower exclusions.

`ALKScript.Interpreter.Serialization.CursorStructuralStateSerializer.Capture`/
`Restore` provide the JSON `byte[]` round trip, mirroring
`CursorStateSerializer` — `SerializedStructuralCaptureState` and its nested
DTOs (`SerializedHeapEntry`, `SerializedEnvironment`, `SerializedTrailEntry`,
`SerializedPendingAwait`, `SerializedAwaitElement`, `SerializedAstReference`,
`SerializedToken`, `SerializedTypeNode`) convert every type above to/from its
wire shape, reusing `SerializedValue`/`SerializedOperation` for primitives and
operation descriptors.

### 6.3 Pending operations in locals

Closes a gap in §6.2 where any `PendingOperationValue`/`ThunkValue` reachable
from a local variable other than the suspending await's own operand threw
`NotSupportedException` — the common pattern `var op = startLongTask(); ...
await op;` (potentially across many ticks, Captured/Restored before the
`await` is ever reached).

- `CursorStructuralCaptureState.PendingOperations` is a top-level table
  (`List<CapturedPendingOperation>`), analogous to `Heap`. Every
  `PendingOperationValue`/`ThunkValue` reachable from a local — and the
  cursor's own awaited operand, via `CapturedPendingAwait.OperationRef` — is
  captured here, two-pass-deduplicated by reference identity exactly like
  `Heap`/`GetHeapId` (`pendingOpIds`/`GetPendingOpId`). The *same* underlying
  instance referenced from both a local `op` and the suspending `await op`
  resolves to a single shared table entry, so Restore reconstructs/reissues it
  exactly once.
- Each `CapturedPendingOperation` is `{ Element: CapturedAwaitElement,
  WasStarted: bool }`, reusing §6.2's `CapturedAwaitElement` union:
  - `Resolved` — a `ThunkValue` whose task already ran to completion, or a
    `PendingOperationValue` whose started task completed successfully.
    Reconstructed via `ThunkValue.FromResult`.
  - `Fault` — a `ThunkValue`/`PendingOperationValue` whose task is faulted.
    Reconstructed as a `ThunkValue` wrapping `Task.FromException`.
  - `Reissue` — a `PendingOperationValue` with a recoverable
    `PendingOperation`. `WasStarted` records whether `Start()` had already
    been called at capture time (even if not yet settled):
    - `WasStarted = false` (e.g. `var op = startLongTask();` before any
      `await op`) — Restore constructs a fresh, **not-started**
      `PendingOperationValue`; the script's own later `await op` triggers the
      actual `Start` call.
    - `WasStarted = true` — Restore eagerly calls `Start` during
      reconstruction (mirroring §6.2's `Reissue` handling for the cursor's own
      operand) and registers the result with
      `FunctionValueFactory.RegisterRestored` so end-of-script
      `DiscardPending` doesn't double-discard it.
- **Composite-element aliasing**: a composite `await [a, b, c]` element whose
  underlying `PendingOperationValue`/`ThunkValue` instance is *also*
  referenced from a local (e.g. `var op = fetch(); var r = await [op, 5];`) is
  captured as `CapturedAwaitElement.OperationRef(id, elementType)` — the
  element's `AwaitElement.Source` is looked up in `pendingOpIds` (already
  populated by the local's own `GetPendingOpId` call, since environments are
  captured before `PendingAwait`) and, if present, shares that
  `PendingOperations` table entry instead of capturing a second, independent
  `Reissue`. A non-aliased composite element is captured exactly as before
  (`Resolved`/`Reissue`/`Fault`, independent of the table). On Restore,
  `OperationRef` elements are rebuilt from the already-reconstructed
  `PendingOperations[id]` value — a `PendingOperationValue` (guaranteed
  started, since composite elements are always eagerly started) contributes
  its started task, a `ThunkValue` contributes its task — via
  `AwaitElement.ForTask`, so `Start` is called exactly once and `op`/the
  composite element observably refer to the same reconstructed operation.
- **Exclusion** (`NotSupportedException` at Capture time): a still-*pending*
  `ThunkValue` with no backing `PendingOperationValue` — fundamental, not just
  unimplemented, since `ThunkValue` carries no `PendingOperation` descriptor
  for Restore to reissue.
- Wire format: `SerializedStructuralCaptureState.PendingOperations` (a list of
  `SerializedPendingOperation { Element: SerializedAwaitElement, WasStarted:
  bool }`), `SerializedHeapValue`'s `"pendingopref"` kind,
  `SerializedAwaitElement`'s `"operationref"` kind, and
  `SerializedPendingAwait.OperationRef`.

---

## 7. Known limitations and future work

- **Native array-method callbacks with `await`** (`arr.map(x => await
  fetch(x))`): `map`/`filter` are implemented via `CursorCallInvoker.Call(callback,
  ...)`, a per-item, native-driven loop. There is currently no trail entry
  recording "I am partway through a native `map`/`filter` loop, here is my
  source array, my current index, and my accumulated results so far", so
  `CaptureStructural()` cannot snapshot an in-progress native loop, and a
  callback that suspends is unsupported.

  A future implementation could add a `TrailEntry.NativeLoopFrame`:

  ```
  TrailEntry.NativeLoopFrame {
    MethodName: "map" | "filter",
    SourceArray: HeapRef,           // the receiver array being iterated
    CurrentIndex: int,               // index of the element whose callback is in flight
    Accumulated: IReadOnlyList<CapturedHeapValue>, // results collected so far
    Callback: CapturedHeapValue,     // Method / AstRef — NOT a lambda
  }
  ```

  `RestoreStructural` would graft this back onto the trail, and
  `CursorCallInvoker`'s `map`/`filter` driver would resume from
  `CurrentIndex`, re-invoking `Callback` for the in-flight element (the
  suspended invocation itself is represented by the trail frames above the
  `NativeLoopFrame`) and continuing to populate `Accumulated`.

  Constraints for that future work:
  - The callback must be capturable as a `Method`/`AstRef` reference — a
    `map`/`filter` callback that is itself a lambda closing over local state
    remains excluded (same as the existing
    `CaptureStructural_LocalBoundToLambdaValue_Throws` exclusion).
  - As with `CursorCallInvoker.Construct`'s precedent for `new`, the receiver
    array expression (and any arguments to `map`/`filter` itself) must be safe
    to re-evaluate if the enclosing statement is re-executed on resume (§3).
  - This is a special case of a more general "checkpoint a partially-evaluated
    expression" mechanism that would be needed to lift §3's restriction that
    `await` may only appear in a small set of statement-level positions. If
    that more general mechanism is ever pursued, `NativeLoopFrame` should be
    designed as a special case of it (or implemented first, as the simpler,
    narrower case that validates the general approach).

---

## 8. Replay-sensitivity classification

§6.1's operation log does not record "every native call" — only calls whose
result could legitimately differ between the original run and a replay. This
is a strict superset of "every `await`ed async operation" (those always
qualify) but excludes calls that are pure functions of their arguments
(`Math.sqrt`, string formatting, array indexing, ...), which can simply be
re-executed during replay with identical results at zero log cost. The
interesting middle ground is **synchronous** natives that are still
non-deterministic (wall-clock time, randomness, live mutable game state) —
these need logging despite not being `async`. This mirrors the
activities-vs-deterministic-workflow-code split used by Temporal/Durable
Functions.

This classification is **not** a grammar feature (no `[NonDeterministic]`-style
modifier in `.alk` source) — a logged and an unlogged native call look and
return identically to the script; only what the *runtime* persists changes.
It is purely a host/runtime contract: when the host registers the
implementation for a `native` declaration, it also supplies metadata (e.g. an
`IsReplaySensitive` flag or a parallel registration table) declaring whether
that implementation's results must be recorded for replay. This keeps the
grammar entirely script-author-facing and lets the host re-classify a native
later (e.g. discovering a "pure" one secretly reads live state) without
touching or recompiling any script. Every `native async` declaration is
inherently replay-sensitive by definition, so this metadata only needs to
cover the narrower set of non-deterministic *synchronous* natives.
