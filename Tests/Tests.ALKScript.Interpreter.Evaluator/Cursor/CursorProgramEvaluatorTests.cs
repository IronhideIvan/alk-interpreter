using System;
using System.Collections.Generic;
using ALKScript.Interpreter.Common.Evaluation.Scheduling;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Evaluator;
using ALKScript.Interpreter.Evaluator.Cursor;
using ALKScript.Interpreter.Parser;

namespace Tests.ALKScript.Interpreter.Evaluator.Cursor;

/// <summary>
/// Step 8 coverage for the cursor-rewrite plan (docs:
/// validated-nibbling-narwhal): <see cref="CursorProgramEvaluator"/>'s
/// module-graph traversal — running global preludes then the entry module's
/// top-level declarations via <see cref="EvaluationCursor.Start"/>/<see cref="EvaluationCursor.Resume"/>,
/// preserving the old evaluator's "DiscardPending" end-of-script sweep, the
/// uncaught-throw-to-<see cref="RuntimeException"/> behavior, and single-thunk
/// <c>await</c> suspension at the program level.
/// </summary>
public class CursorProgramEvaluatorTests : EvaluatorTestBase
{
  private static IReadOnlyList<ALKScriptValue> Run(string source, IReadOnlyList<string>? globalPreludeSources = null)
  {
    var recorded = new List<ALKScriptValue>();
    var bindings = new ScriptNativeBindings
    {
      ["record"] = arguments => { recorded.Add(arguments[0]); return NullValue.Instance; }
    };

    var graph = LoadGraph(source, globalPreludeSources);
    var evaluator = new CursorProgramEvaluator(bindings);

    Assert.Equal(ProgramRunResult.Completed, evaluator.Evaluate(graph));
    return recorded;
  }

  [Fact]
  public void Evaluate_SimpleArithmetic_RecordsResult()
  {
    var recorded = Run($"{RecordDeclaration}\nrecord(1 + 2);");

    Assert.Equal(3L, Assert.IsType<IntValue>(recorded[0]).Value);
  }

  [Fact]
  public void Evaluate_GlobalPrelude_DeclarationsAreVisibleToTheEntryModule()
  {
    var recorded = Run(
      $"{RecordDeclaration}\nrecord(answer());",
      globalPreludeSources: new[] { "function int answer() { return 42; }" });

    Assert.Equal(42L, Assert.IsType<IntValue>(recorded[0]).Value);
  }

  [Fact]
  public void Evaluate_UncaughtThrow_ThrowsRuntimeException()
  {
    var graph = LoadGraph($"{RecordDeclaration}\nthrow \"boom\";");
    var bindings = new ScriptNativeBindings
    {
      ["record"] = arguments => NullValue.Instance
    };
    var evaluator = new CursorProgramEvaluator(bindings);

    var exception = Assert.Throws<RuntimeException>(() => evaluator.Evaluate(graph));
    Assert.Contains("boom", exception.Message);
  }

  [Fact]
  public void Evaluate_TopLevelAwaitOnUnresolvedThunk_ReturnsAwaitingThenResumes()
  {
    var source = $"{RecordDeclaration}\nnative function thunk<int> fetch();\nvar x = await fetch();\nrecord(x);";

    var recorded = new List<ALKScriptValue>();
    var bindings = new ScriptNativeBindings
    {
      ["record"] = arguments => { recorded.Add(arguments[0]); return NullValue.Instance; }
    };

    var binder = new LiveBinder(_ => null);

    var graph = LoadGraph(source);
    var evaluator = new CursorProgramEvaluator(bindings, operationBinder: binder);

    Assert.Equal(ProgramRunResult.Awaiting, evaluator.Evaluate(graph));
    Assert.Empty(recorded);

    var result = evaluator.Resume(new IntValue(7));

    Assert.Equal(ProgramRunResult.Completed, result);
    Assert.Equal(7L, Assert.IsType<IntValue>(recorded[0]).Value);
  }

  [Fact]
  public void Evaluate_AwaitOnAlreadyResolvedPendingOperation_RecordsItsResultToTheReplayLog()
  {
    var source = $"{RecordDeclaration}\nnative function thunk<int> fetch();\nvar x = await fetch();\nrecord(x);";

    var bindings = new ScriptNativeBindings
    {
      ["record"] = arguments => NullValue.Instance
    };

    var binder = new LiveBinder(_ => new OperationStatus.Resolved(new IntValue(9)));

    var graph = LoadGraph(source);
    var evaluator = new CursorProgramEvaluator(bindings, operationBinder: binder);

    Assert.Equal(ProgramRunResult.Completed, evaluator.Evaluate(graph));

    Assert.Single(evaluator.Log);
    Assert.Equal("fetch", evaluator.Log[0].Operation.Name);
    Assert.Equal(9L, Assert.IsType<IntValue>(evaluator.Log[0].Result!).Value);
  }

  [Fact]
  public void Evaluate_FunctionSuspendsMidBody_ReturnsAwaitingThenResumes()
  {
    var source = $"{RecordDeclaration}\nnative function thunk<int> fetch();\nfunction int foo() {{\n  var x = await fetch();\n  return x + 1;\n}}\nvar y = foo();\nrecord(y);";

    var recorded = new List<ALKScriptValue>();
    var bindings = new ScriptNativeBindings
    {
      ["record"] = arguments => { recorded.Add(arguments[0]); return NullValue.Instance; }
    };

    var binder = new LiveBinder(_ => null);

    var graph = LoadGraph(source);
    var evaluator = new CursorProgramEvaluator(bindings, operationBinder: binder);

    Assert.Equal(ProgramRunResult.Awaiting, evaluator.Evaluate(graph));
    Assert.Empty(recorded);

    var result = evaluator.Resume(new IntValue(41));

    Assert.Equal(ProgramRunResult.Completed, result);
    Assert.Equal(42L, Assert.IsType<IntValue>(recorded[0]).Value);
  }

  [Fact]
  public void Evaluate_MethodSuspendsMidBody_ThisSurvivesResume()
  {
    var source = $"{RecordDeclaration}\nnative function thunk<int> fetch();\nclass Box {{\n  public int v;\n  new(int v) {{\n    this.v = v;\n  }}\n  public function int getPlusFetched() {{\n    var x = await fetch();\n    return this.v + x;\n  }}\n}}\nvar b = new Box(5);\nvar y = b.getPlusFetched();\nrecord(y);";

    var recorded = new List<ALKScriptValue>();
    var bindings = new ScriptNativeBindings
    {
      ["record"] = arguments => { recorded.Add(arguments[0]); return NullValue.Instance; }
    };

    var binder = new LiveBinder(_ => null);

    var graph = LoadGraph(source);
    var evaluator = new CursorProgramEvaluator(bindings, operationBinder: binder);

    Assert.Equal(ProgramRunResult.Awaiting, evaluator.Evaluate(graph));
    Assert.Empty(recorded);

    var result = evaluator.Resume(new IntValue(10));

    Assert.Equal(ProgramRunResult.Completed, result);
    Assert.Equal(15L, Assert.IsType<IntValue>(recorded[0]).Value);
  }

  [Fact]
  public void Evaluate_ConstructorSuspendsMidBody_ReturnsAwaitingThenResumes()
  {
    var source = $"{RecordDeclaration}\nnative function thunk<int> fetch();\nclass Box {{\n  public int v;\n  new() {{\n    var x = await fetch();\n    this.v = x;\n  }}\n}}\nvar b = new Box();\nrecord(b.v);";

    var recorded = new List<ALKScriptValue>();
    var bindings = new ScriptNativeBindings
    {
      ["record"] = arguments => { recorded.Add(arguments[0]); return NullValue.Instance; }
    };

    var binder = new LiveBinder(_ => null);

    var graph = LoadGraph(source);
    var evaluator = new CursorProgramEvaluator(bindings, operationBinder: binder);

    Assert.Equal(ProgramRunResult.Awaiting, evaluator.Evaluate(graph));
    Assert.Empty(recorded);

    var result = evaluator.Resume(new IntValue(10));

    Assert.Equal(ProgramRunResult.Completed, result);
    Assert.Equal(10L, Assert.IsType<IntValue>(recorded[0]).Value);
  }

  [Fact]
  public void Evaluate_FieldInitializerAwait_IsAParseTimeError()
  {
    // Per LANGUAGE_SPEC.md §8.1, 'await' is only allowed as the entire
    // initializer of a 'var' declaration, the entire value of a
    // 'return'/'throw' statement, or the entire expression of an expression
    // statement — a field initializer is none of those, so this is now a
    // parse-time error rather than a RuntimeException at evaluation time.
    var source = $"{RecordDeclaration}\nnative function thunk<int> fetch();\nclass Box {{\n  public int v = await fetch();\n}}\nvar b = new Box();\nrecord(b.v);";

    Assert.Throws<ParseException>(() => LoadGraph(source));
  }

  [Fact]
  public void Evaluate_NestedCallSuspends_PropagatesThroughBothFrames()
  {
    var source = $"{RecordDeclaration}\nnative function thunk<int> fetch();\nfunction int inner() {{\n  var x = await fetch();\n  return x + 1;\n}}\nfunction int outer() {{\n  var y = inner();\n  return y + 1;\n}}\nvar z = outer();\nrecord(z);";

    var recorded = new List<ALKScriptValue>();
    var bindings = new ScriptNativeBindings
    {
      ["record"] = arguments => { recorded.Add(arguments[0]); return NullValue.Instance; }
    };

    var binder = new LiveBinder(_ => null);

    var graph = LoadGraph(source);
    var evaluator = new CursorProgramEvaluator(bindings, operationBinder: binder);

    Assert.Equal(ProgramRunResult.Awaiting, evaluator.Evaluate(graph));
    Assert.Empty(recorded);

    var result = evaluator.Resume(new IntValue(10));

    Assert.Equal(ProgramRunResult.Completed, result);
    Assert.Equal(12L, Assert.IsType<IntValue>(recorded[0]).Value);
  }

  /// <summary>
  /// Pins the documented limitation (docs/ASYNC_AWAIT_DESIGN.md Addendum 3):
  /// when the statement containing a suspending call is re-executed on
  /// resume, its argument expressions are re-evaluated from scratch. Here
  /// <c>sideEffect()</c> runs again first (so <c>calls</c> ends at 2) —
  /// while doing so it itself runs through a fresh call frame that is
  /// still "resuming" (the resume trail hasn't been fully unwound yet), so
  /// it consumes the trail entry that was meant for <c>withAwait</c>'s
  /// suspended body. <c>withAwait</c> is then entered fresh with the
  /// *second* invocation's argument (<c>v == 2</c>), so the result is
  /// <c>v + x == 2 + 10 == 12</c>, not the <c>11</c> a naive reading of the
  /// trail might suggest.
  /// </summary>
  [Fact]
  public void Evaluate_SuspendingCallWithSideEffectingArgument_ArgumentRunsTwice()
  {
    var source = $"{RecordDeclaration}\nnative function thunk<int> fetch();\nvar calls = 0;\nfunction int sideEffect() {{\n  calls = calls + 1;\n  return calls;\n}}\nfunction int withAwait(int v) {{\n  var x = await fetch();\n  return v + x;\n}}\nvar y = withAwait(sideEffect());\nrecord(y);\nrecord(calls);";

    var recorded = new List<ALKScriptValue>();
    var bindings = new ScriptNativeBindings
    {
      ["record"] = arguments => { recorded.Add(arguments[0]); return NullValue.Instance; }
    };

    var binder = new LiveBinder(_ => null);

    var graph = LoadGraph(source);
    var evaluator = new CursorProgramEvaluator(bindings, operationBinder: binder);

    Assert.Equal(ProgramRunResult.Awaiting, evaluator.Evaluate(graph));
    Assert.Empty(recorded);

    var result = evaluator.Resume(new IntValue(10));

    Assert.Equal(ProgramRunResult.Completed, result);
    Assert.Equal(12L, Assert.IsType<IntValue>(recorded[0]).Value);
    Assert.Equal(2L, Assert.IsType<IntValue>(recorded[1]).Value);
  }

  // ---------------------------------------------------------------------
  // Composite whenAll suspension — await [a, b, ...] where one or more
  // elements are genuinely in-flight (Step 11 of the cursor-rewrite plan).
  // ---------------------------------------------------------------------

  [Fact]
  public void Evaluate_AwaitOnArrayOfPendingOperations_ReturnsAwaitingThenResumesWithArrayOfResolvedValues()
  {
    var source = $"{RecordDeclaration}\nnative function thunk<int> fetchA();\nnative function thunk<int> fetchB();\nvar results = await [fetchA(), fetchB()];\nrecord(results[0]);\nrecord(results[1]);";

    var recorded = new List<ALKScriptValue>();
    var bindings = new ScriptNativeBindings
    {
      ["record"] = arguments => { recorded.Add(arguments[0]); return NullValue.Instance; }
    };

    var binder = new LiveBinder(_ => null);

    var graph = LoadGraph(source);
    var evaluator = new CursorProgramEvaluator(bindings, operationBinder: binder);

    Assert.Equal(ProgramRunResult.Awaiting, evaluator.Evaluate(graph));
    Assert.Empty(recorded);
    Assert.NotNull(evaluator.PendingAwait!.CompositeElements);
    Assert.Equal(2, evaluator.PendingAwait!.CompositeElements!.Count);

    binder.Settle("fetchA", new IntValue(1));
    binder.Settle("fetchB", new IntValue(2));
    PollPendingElements(evaluator);

    var result = evaluator.Resume(NullValue.Instance);

    Assert.Equal(ProgramRunResult.Completed, result);
    Assert.Equal(1L, Assert.IsType<IntValue>(recorded[0]).Value);
    Assert.Equal(2L, Assert.IsType<IntValue>(recorded[1]).Value);
    Assert.Equal(2, evaluator.Log.Count);
  }

  [Fact]
  public void Evaluate_AwaitOnArrayWhereOneFaults_SurfacesAggregateThrownSignal()
  {
    var source = $"{RecordDeclaration}\nnative function thunk<int> ok();\nnative function thunk<int> fail();\ntry {{\n  await [ok(), fail()];\n}} catch (string e) {{\n  record(e);\n}}";

    var recorded = new List<ALKScriptValue>();
    var bindings = new ScriptNativeBindings
    {
      ["record"] = arguments => { recorded.Add(arguments[0]); return NullValue.Instance; }
    };

    var binder = new LiveBinder(_ => null);

    var graph = LoadGraph(source);
    var evaluator = new CursorProgramEvaluator(bindings, operationBinder: binder);

    Assert.Equal(ProgramRunResult.Awaiting, evaluator.Evaluate(graph));

    binder.Settle("ok", new IntValue(99));
    binder.SettleFault("fail", new InvalidOperationException("boom"));
    PollPendingElements(evaluator);

    var result = evaluator.Resume(NullValue.Instance);

    Assert.Equal(ProgramRunResult.Completed, result);
    var fault = Assert.IsType<StringValue>(Assert.Single(recorded));
    Assert.Equal("boom", fault.Value);
  }

  [Fact]
  public void Evaluate_AwaitOnArrayWhereBothFault_AggregatesMessages()
  {
    var source = $"{RecordDeclaration}\nnative function thunk<int> a();\nnative function thunk<int> b();\ntry {{\n  await [a(), b()];\n}} catch (string e) {{\n  record(e);\n}}";

    var recorded = new List<ALKScriptValue>();
    var bindings = new ScriptNativeBindings
    {
      ["record"] = arguments => { recorded.Add(arguments[0]); return NullValue.Instance; }
    };

    var binder = new LiveBinder(_ => null);

    var graph = LoadGraph(source);
    var evaluator = new CursorProgramEvaluator(bindings, operationBinder: binder);

    Assert.Equal(ProgramRunResult.Awaiting, evaluator.Evaluate(graph));

    binder.SettleFault("a", new InvalidOperationException("fault-a"));
    binder.SettleFault("b", new InvalidOperationException("fault-b"));
    PollPendingElements(evaluator);

    var result = evaluator.Resume(NullValue.Instance);

    Assert.Equal(ProgramRunResult.Completed, result);
    var fault = Assert.IsType<StringValue>(Assert.Single(recorded));
    Assert.Contains("fault-a", fault.Value);
    Assert.Contains("fault-b", fault.Value);
  }

  [Fact]
  public void Evaluate_AwaitOnArrayFault_ReportsEachFaultedOperationToHost()
  {
    var source = $"{RecordDeclaration}\nnative function thunk<int> a();\nnative function thunk<int> b();\ntry {{\n  await [a(), b()];\n}} catch (string e) {{\n}}";

    var bindings = new ScriptNativeBindings
    {
      ["record"] = arguments => NullValue.Instance
    };

    var binder = new LiveBinder(_ => null);

    var graph = LoadGraph(source);
    var evaluator = new CursorProgramEvaluator(bindings, operationBinder: binder);

    Assert.Equal(ProgramRunResult.Awaiting, evaluator.Evaluate(graph));

    binder.SettleFault("a", new InvalidOperationException("fault-a"));
    binder.SettleFault("b", new InvalidOperationException("fault-b"));
    PollPendingElements(evaluator);

    Assert.Equal(ProgramRunResult.Completed, evaluator.Resume(NullValue.Instance));

    Assert.Equal(2, binder.ReportedFaults.Count);
    Assert.Contains(binder.ReportedFaults, f => f.Operation.Name == "a");
    Assert.Contains(binder.ReportedFaults, f => f.Operation.Name == "b");
  }

  [Fact]
  public void Evaluate_AwaitOnArrayWithMixOfResolvedAndPendingElements_ResolvesAfterResume()
  {
    var source = $"{RecordDeclaration}\nnative function thunk<int> resolved();\nnative function thunk<int> pending();\nvar results = await [resolved(), pending()];\nrecord(results[0]);\nrecord(results[1]);";

    var recorded = new List<ALKScriptValue>();
    var bindings = new ScriptNativeBindings
    {
      ["record"] = arguments => { recorded.Add(arguments[0]); return NullValue.Instance; }
    };

    var binder = new LiveBinder(op => op.Name == "resolved" ? new OperationStatus.Resolved(new IntValue(5)) : null);

    var graph = LoadGraph(source);
    var evaluator = new CursorProgramEvaluator(bindings, operationBinder: binder);

    Assert.Equal(ProgramRunResult.Awaiting, evaluator.Evaluate(graph));

    binder.Settle("pending", new IntValue(6));
    PollPendingElements(evaluator);

    var result = evaluator.Resume(NullValue.Instance);

    Assert.Equal(ProgramRunResult.Completed, result);
    Assert.Equal(5L, Assert.IsType<IntValue>(recorded[0]).Value);
    Assert.Equal(6L, Assert.IsType<IntValue>(recorded[1]).Value);
  }

  [Fact]
  public void Evaluate_AwaitOnArrayAllAlreadyResolved_DoesNotSuspend()
  {
    var source = $"{RecordDeclaration}\nnative function thunk<int> a();\nnative function thunk<int> b();\nvar results = await [a(), b()];\nrecord(results[0]);\nrecord(results[1]);";

    var recorded = new List<ALKScriptValue>();
    var bindings = new ScriptNativeBindings
    {
      ["record"] = arguments => { recorded.Add(arguments[0]); return NullValue.Instance; }
    };

    var binder = new LiveBinder(op => op.Name == "a"
      ? new OperationStatus.Resolved(new IntValue(1))
      : new OperationStatus.Resolved(new IntValue(2)));

    var graph = LoadGraph(source);
    var evaluator = new CursorProgramEvaluator(bindings, operationBinder: binder);

    Assert.Equal(ProgramRunResult.Completed, evaluator.Evaluate(graph));
    Assert.Equal(1L, Assert.IsType<IntValue>(recorded[0]).Value);
    Assert.Equal(2L, Assert.IsType<IntValue>(recorded[1]).Value);
  }

  /// <summary>
  /// Polls every composite element's <see cref="PendingOperationValue"/> (if
  /// any) so that operations the test settled via <see cref="LiveBinder.Settle"/>/
  /// <see cref="LiveBinder.SettleFault"/> after suspension have their
  /// <see cref="PendingOperationValue.Status"/> refreshed before
  /// <see cref="CursorProgramEvaluator.Resume"/> calls <c>ResolveWhenAll</c> —
  /// mirroring what a host's <see cref="ProgramRun.Pump"/> would do.
  /// </summary>
  private static void PollPendingElements(CursorProgramEvaluator evaluator)
  {
    if (evaluator.PendingAwait?.CompositeElements == null) return;

    foreach (var element in evaluator.PendingAwait.CompositeElements)
    {
      (element.Source as PendingOperationValue)?.Poll();
    }
  }

  /// <summary>
  /// A binder whose <see cref="Start"/> reports <paramref name="seed"/>'s
  /// status for an operation if non-null, otherwise <see cref="OperationStatus.Pending"/>;
  /// <see cref="Poll"/> re-checks a settlement map that <see cref="Settle"/>/
  /// <see cref="SettleFault"/> mutate from the test, mirroring
  /// <c>PendingPumpTests.PollBinder</c>.
  /// </summary>
  private sealed class LiveBinder : IAsyncOperationBinder
  {
    private readonly Func<PendingOperation, OperationStatus?> _seed;
    private readonly Dictionary<string, OperationStatus> _settled = new();

    internal readonly List<(PendingOperation Operation, Exception Fault)> ReportedFaults = new List<(PendingOperation, Exception)>();

    internal LiveBinder(Func<PendingOperation, OperationStatus?> seed) => _seed = seed;

    internal void Settle(string operationName, ALKScriptValue value) => _settled[operationName] = new OperationStatus.Resolved(value);

    internal void SettleFault(string operationName, Exception error) => _settled[operationName] = new OperationStatus.Faulted(error);

    public OperationStatus Start(PendingOperation operation)
    {
      var seeded = _seed(operation);
      if (seeded != null)
      {
        _settled[operation.Name] = seeded;
        return seeded;
      }

      return OperationStatus.Pending.Instance;
    }

    public OperationStatus Poll(PendingOperation operation) =>
      _settled.TryGetValue(operation.Name, out var status) ? status : OperationStatus.Pending.Instance;

    public void Discard(PendingOperation operation, Action<Exception> onFault) { }

    public void OnOperationFaulted(PendingOperation operation, Exception fault)
      => ReportedFaults.Add((operation, fault));
  }
}
