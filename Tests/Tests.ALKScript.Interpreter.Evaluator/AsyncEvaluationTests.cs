using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ALKScript.Interpreter.Common.Evaluation.Scheduling;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Evaluator;

namespace Tests.ALKScript.Interpreter.Evaluator;

/// <summary>
/// End-to-end coverage for "real" <c>await</c> suspension (see
/// docs/ASYNC_AWAIT_DESIGN.md): an <c>await</c> on a <see cref="ThunkValue"/>
/// or <see cref="PendingOperationValue"/> (the shapes a <c>thunk</c>/<c>thunk&lt;T&gt;</c>-typed
/// expression evaluates to) genuinely parks evaluation on the underlying
/// <see cref="System.Threading.Tasks.Task"/> — including across a real
/// cross-thread completion — and resumes with either the produced value or a
/// catchable fault.
/// </summary>
public class AsyncEvaluationTests : EvaluatorTestBase
{
  [Fact]
  public void Evaluate_AwaitOnNativeReturningAlreadyCompletedTask_ResolvesToItsValue()
  {
    var recorded = Run(
      $"{RecordDeclaration}\nnative function thunk<int> fetch();\nfunction void main() {{\n  var fetched = await fetch();\n  record(fetched);\n}}\nmain();",
      new FuncBinder(_ => Task.FromResult((ALKScriptValue)new IntValue(42))));

    var value = Assert.IsType<IntValue>(Assert.Single(recorded));
    Assert.Equal(42L, value.Value);
  }

  [Fact]
  public void Evaluate_AwaitOnPendingTaskCompletedFromBackgroundThread_SuspendsAndResumesWithItsValue()
  {
    // "fetch" hands back a *pending* task — "await" must genuinely suspend
    // mid-script on it (there's nothing to resolve to yet) and resume once a
    // background thread settles it.
    var recorded = Run(
      $"{RecordDeclaration}\nnative function thunk<int> fetch();\nfunction void main() {{\n  var fetched = await fetch();\n  record(fetched);\n}}\nmain();",
      new FuncBinder(_ => Task.Run(async () =>
      {
        await Task.Delay(10);
        return (ALKScriptValue)new IntValue(7);
      })));

    var value = Assert.IsType<IntValue>(Assert.Single(recorded));
    Assert.Equal(7L, value.Value);
  }

  [Fact]
  public void Evaluate_AwaitOnPendingTaskFaultedFromBackgroundThread_RaisesACatchableThrownSignal()
  {
    var recorded = Run(
      $"{RecordDeclaration}\nnative function thunk<int> fetch();\nfunction void main() {{\n  try {{\n    await fetch();\n  }} catch (string e) {{\n    record(e);\n  }}\n}}\nmain();",
      new FuncBinder(_ => Task.Run<ALKScriptValue>(async () =>
      {
        await Task.Delay(10);
        throw new System.InvalidOperationException("boom");
      })));

    var value = Assert.IsType<StringValue>(Assert.Single(recorded));
    Assert.Equal("boom", value.Value);
  }

  [Fact]
  public void Evaluate_AwaitOnPlainValue_YieldsItDirectly()
  {
    var recorded = Run($"{RecordDeclaration}\nfunction void main() {{\n  var awaited = await 1;\n  record(awaited);\n}}\nmain();");

    var value = Assert.IsType<IntValue>(Assert.Single(recorded));
    Assert.Equal(1L, value.Value);
  }

  [Fact]
  public void Evaluate_AwaitOnTaskCompletedFromABackgroundThread_SuspendsAndSafelyResumesOnTheScriptThread()
  {
    // This is the scenario Phase 3 had to work around (see its "known
    // limitation" tests' comments): a native hands back a task that genuinely
    // settles on a background thread — via Task.Run — fully concurrently with
    // the script. Without a scheduler, that continuation would either run
    // unobserved (the driver had already returned) or race the script on
    // whatever thread it happened to land on. Now that EvaluatorTestBase
    // drives evaluation through ScriptScheduler.RunUntilComplete, the
    // continuation is Post()ed back to — and only ever runs on — the single
    // host thread that's pumping the script, exactly like every other
    // continuation: genuinely concurrent settlement, but cooperative,
    // single-threaded resumption.
    //
    // NB: this awaits directly at the top level rather than from inside an
    // "async function main()" — top-level statements run as part of the
    // overall (also Task-returning) program evaluation, so awaiting here
    // suspends *that* task, and RunUntilComplete genuinely waits for it.
    // (Awaiting only from within a fire-and-forget "async" call wouldn't:
    // per the eager-start model such a call returns immediately, so the
    // top-level "Evaluate" task — and thus RunUntilComplete — would
    // consider the program already finished. Observing a fire-and-forget
    // async call's *eventual* completion is exactly the "Discard"/lazy-start
    // mechanism the design defers past this phase.)
    var recorded = Run(
      $"{RecordDeclaration}\nnative function thunk<int> fetch();\nvar fetched = await fetch();\nrecord(fetched);",
      new FuncBinder(_ => Task.Run(async () =>
      {
        await Task.Delay(20);
        return (ALKScriptValue)new IntValue(99);
      })));

    var value = Assert.IsType<IntValue>(Assert.Single(recorded));
    Assert.Equal(99L, value.Value);
  }

  // -------------------------------------------------------------------------
  // Discard
  // -------------------------------------------------------------------------

  [Fact]
  public void Evaluate_CallingAsyncNativeWithoutAwaiting_DiscardsItAtEndOfScript()
  {
    // Calling "move()" without "await" must NOT start the operation eagerly
    // (lazy/deferred-start guarantee). When the script ends, ProgramEvaluator
    // sweeps all un-started PendingOperationValues and calls Discard on each —
    // which is what the host uses to fire them off as fire-and-forget effects.
    var binder = new FuncBinder(_ => Task.FromResult((ALKScriptValue)NullValue.Instance));

    RunWithOperationBinder("native function thunk move(); move();", null, binder);

    var discarded = Assert.Single(binder.Discarded);
    Assert.Equal("move", discarded.Name);
  }

  [Fact]
  public void Evaluate_AwaitedAsyncNative_IsNotDiscardedAtEndOfScript()
  {
    // An operation that was already started via "await" must NOT be discarded
    // a second time at end-of-script (HasStarted guards this).
    var binder = new FuncBinder(_ => Task.FromResult((ALKScriptValue)NullValue.Instance));

    RunWithOperationBinder("native function thunk move(); await move();", null, binder);

    Assert.Empty(binder.Discarded);
  }

  // -------------------------------------------------------------------------
  // whenAll — await [a, b, …]
  // -------------------------------------------------------------------------

  [Fact]
  public void Evaluate_AwaitOnArrayOfPendingOperations_ReturnsArrayOfResolvedValues()
  {
    // "await [fetchA(), fetchB()]" — both operations resolve; result is an
    // ArrayValue of their resolved values, in element order.
    var binder = new FuncBinder(op => op.Name == "fetchA"
      ? Task.FromResult((ALKScriptValue)new IntValue(1))
      : Task.FromResult((ALKScriptValue)new IntValue(2)));

    var recorded = Run(
      $"{RecordDeclaration}\nnative function thunk<int> fetchA();\nnative function thunk<int> fetchB();\nfunction void main() {{\n  var results = await [fetchA(), fetchB()];\n  record(results[0]);\n  record(results[1]);\n}}\nmain();",
      binder);

    Assert.Equal(2, recorded.Count);
    Assert.Equal(1L, Assert.IsType<IntValue>(recorded[0]).Value);
    Assert.Equal(2L, Assert.IsType<IntValue>(recorded[1]).Value);
  }

  [Fact]
  public void Evaluate_AwaitOnArrayWhereOneFaults_SurfacesAggregateThrownSignal()
  {
    // One operation faults — the script's "catch" sees the fault message, and
    // run-to-completion means the healthy operation still ran to completion.
    var pending = new TaskCompletionSource<ALKScriptValue>();
    var binder = new FuncBinder(op => op.Name == "ok"
      ? Task.FromResult((ALKScriptValue)new IntValue(99))
      : Task.FromException<ALKScriptValue>(new InvalidOperationException("boom")));

    var recorded = Run(
      $"{RecordDeclaration}\nnative function thunk<int> ok();\nnative function thunk<int> fail();\nfunction void main() {{\n  try {{\n    await [ok(), fail()];\n  }} catch (string e) {{\n    record(e);\n  }}\n}}\nmain();",
      binder);

    var fault = Assert.IsType<StringValue>(Assert.Single(recorded));
    Assert.Equal("boom", fault.Value);
  }

  [Fact]
  public void Evaluate_AwaitOnArrayWhereBothFault_AggregatesMesages()
  {
    var binder = new FuncBinder(op => Task.FromException<ALKScriptValue>(
      new InvalidOperationException(op.Name == "a" ? "fault-a" : "fault-b")));

    var recorded = Run(
      $"{RecordDeclaration}\nnative function thunk<int> a();\nnative function thunk<int> b();\nfunction void main() {{\n  try {{\n    await [a(), b()];\n  }} catch (string e) {{\n    record(e);\n  }}\n}}\nmain();",
      binder);

    var fault = Assert.IsType<StringValue>(Assert.Single(recorded));
    Assert.Contains("fault-a", fault.Value);
    Assert.Contains("fault-b", fault.Value);
  }

  [Fact]
  public void Evaluate_AwaitOnArrayFault_ReportsEachFaultedOperationToHost()
  {
    var binder = new FuncBinder(op => Task.FromException<ALKScriptValue>(
      new InvalidOperationException($"fault-{op.Name}")));

    Run(
      $"{RecordDeclaration}\nnative function thunk<int> a();\nnative function thunk<int> b();\nfunction void main() {{\n  try {{\n    await [a(), b()];\n  }} catch (string e) {{\n  }}\n}}\nmain();",
      binder);

    Assert.Equal(2, binder.ReportedFaults.Count);
    Assert.Contains(binder.ReportedFaults, f => f.Operation.Name == "a");
    Assert.Contains(binder.ReportedFaults, f => f.Operation.Name == "b");
  }

  private static IReadOnlyList<ALKScriptValue> Run(string source, IAsyncOperationBinder operationBinder, ScriptNativeBindings? extraBindings = null)
  {
    var recorded = new List<ALKScriptValue>();
    var bindings = new ScriptNativeBindings(extraBindings ?? new ScriptNativeBindings())
    {
      ["record"] = arguments =>
      {
        recorded.Add(arguments[0]);
        return NullValue.Instance;
      }
    };

    RunWithOperationBinder(source, bindings, operationBinder);

    return recorded;
  }

  private sealed class FuncBinder : IAsyncOperationBinder
  {
    private readonly Func<PendingOperation, Task<ALKScriptValue>> _start;

    internal readonly List<PendingOperation> Discarded = new List<PendingOperation>();
    internal readonly List<(PendingOperation Operation, Exception Fault)> ReportedFaults = new List<(PendingOperation, Exception)>();

    internal FuncBinder(Func<PendingOperation, Task<ALKScriptValue>> start) => _start = start;

    public OperationStatus Start(PendingOperation operation)
    {
      try { return new OperationStatus.Resolved(_start(operation).GetAwaiter().GetResult()); }
      catch (Exception ex) { return new OperationStatus.Faulted(ex); }
    }

    public OperationStatus Poll(PendingOperation operation) =>
      throw new InvalidOperationException("Start never returns Pending for this binder.");

    public void Discard(PendingOperation operation, Action<Exception> onFault)
    {
      Discarded.Add(operation);
      _ = _start(operation); // fire-and-forget; fault notification is best-effort in tests
    }

    public void OnOperationFaulted(PendingOperation operation, Exception fault)
      => ReportedFaults.Add((operation, fault));
  }

  [Fact]
  public void Evaluate_FunctionThatAwaitsASuspendingOperation_PropagatesSuspensionToItsCaller()
  {
    // "load"'s own body suspends mid-call (its "await fetch()" parks on a
    // task that settles from a background thread) — proving suspension
    // composes through user-defined functions, not just directly-awaited
    // natives: "main"'s "await load()" is transitively parked until "load"'s
    // body — and therefore "fetch"'s task — settles.
    var recorded = Run(
      $"{RecordDeclaration}\nnative function thunk<int> fetch();\nfunction int load() {{\n  var n = await fetch();\n  return n + 1;\n}}\nfunction void main() {{\n  var loaded = await load();\n  record(loaded);\n}}\nmain();",
      new FuncBinder(_ => Task.Run(async () =>
      {
        await Task.Delay(20);
        return (ALKScriptValue)new IntValue(9);
      })));

    var value = Assert.IsType<IntValue>(Assert.Single(recorded));
    Assert.Equal(10L, value.Value);
  }

  // -------------------------------------------------------------------------
  // thunk<T> result validation
  // -------------------------------------------------------------------------

  [Fact]
  public void Evaluate_AwaitOnThunkOfIntResolvingToString_ThrowsRuntimeException()
  {
    var binder = new FuncBinder(_ => Task.FromResult((ALKScriptValue)new StringValue("oops")));

    Assert.Throws<RuntimeException>(() => Run(
      "native function thunk<int> fetch();\nfunction void main() {\n  await fetch();\n}\nmain();",
      binder));
  }

  [Fact]
  public void Evaluate_AwaitOnThunkOfNonNullableIntResolvingToNull_ThrowsRuntimeException()
  {
    var binder = new FuncBinder(_ => Task.FromResult((ALKScriptValue)NullValue.Instance));

    Assert.Throws<RuntimeException>(() => Run(
      "native function thunk<int> fetch();\nfunction void main() {\n  await fetch();\n}\nmain();",
      binder));
  }

  [Fact]
  public void Evaluate_AwaitOnBareThunkResolvingToNull_DoesNotThrow()
  {
    var binder = new FuncBinder(_ => Task.FromResult((ALKScriptValue)NullValue.Instance));

    RunWithOperationBinder("native function thunk move(); await move();", null, binder);
  }

  [Fact]
  public void Evaluate_AwaitOnArrayWhereOneThunkResolvesToWrongType_ThrowsRuntimeException()
  {
    var binder = new FuncBinder(op => op.Name == "ok"
      ? Task.FromResult((ALKScriptValue)new IntValue(1))
      : Task.FromResult((ALKScriptValue)new StringValue("wrong")));

    Assert.Throws<RuntimeException>(() => Run(
      "native function thunk<int> ok();\nnative function thunk<int> bad();\nfunction void main() {\n  await [ok(), bad()];\n}\nmain();",
      binder));
  }
}
