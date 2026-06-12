using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ALKScript.Interpreter.Common.Evaluation.Scheduling;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Evaluator;
using ALKScript.Interpreter.Evaluator.Cursor;

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

    var source1 = new TaskCompletionSource<ALKScriptValue>();
    var binder = new FuncBinder(_ => source1.Task);

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

    var binder = new FuncBinder(_ => Task.FromResult<ALKScriptValue>(new IntValue(9)));

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

    var source1 = new TaskCompletionSource<ALKScriptValue>();
    var binder = new FuncBinder(_ => source1.Task);

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

    var source1 = new TaskCompletionSource<ALKScriptValue>();
    var binder = new FuncBinder(_ => source1.Task);

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

    var source1 = new TaskCompletionSource<ALKScriptValue>();
    var binder = new FuncBinder(_ => source1.Task);

    var graph = LoadGraph(source);
    var evaluator = new CursorProgramEvaluator(bindings, operationBinder: binder);

    Assert.Equal(ProgramRunResult.Awaiting, evaluator.Evaluate(graph));
    Assert.Empty(recorded);

    var result = evaluator.Resume(new IntValue(10));

    Assert.Equal(ProgramRunResult.Completed, result);
    Assert.Equal(10L, Assert.IsType<IntValue>(recorded[0]).Value);
  }

  [Fact]
  public void Evaluate_FieldInitializerAwait_StillThrowsRuntimeException()
  {
    var source = $"{RecordDeclaration}\nnative function thunk<int> fetch();\nclass Box {{\n  public int v = await fetch();\n}}\nvar b = new Box();\nrecord(b.v);";

    var bindings = new ScriptNativeBindings
    {
      ["record"] = arguments => NullValue.Instance
    };

    var source1 = new TaskCompletionSource<ALKScriptValue>();
    var binder = new FuncBinder(_ => source1.Task);

    var graph = LoadGraph(source);
    var evaluator = new CursorProgramEvaluator(bindings, operationBinder: binder);

    Assert.Throws<RuntimeException>(() => evaluator.Evaluate(graph));
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

    var source1 = new TaskCompletionSource<ALKScriptValue>();
    var binder = new FuncBinder(_ => source1.Task);

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

    var source1 = new TaskCompletionSource<ALKScriptValue>();
    var binder = new FuncBinder(_ => source1.Task);

    var graph = LoadGraph(source);
    var evaluator = new CursorProgramEvaluator(bindings, operationBinder: binder);

    Assert.Equal(ProgramRunResult.Awaiting, evaluator.Evaluate(graph));
    Assert.Empty(recorded);

    var result = evaluator.Resume(new IntValue(10));

    Assert.Equal(ProgramRunResult.Completed, result);
    Assert.Equal(12L, Assert.IsType<IntValue>(recorded[0]).Value);
    Assert.Equal(2L, Assert.IsType<IntValue>(recorded[1]).Value);
  }

  private sealed class FuncBinder : IAsyncOperationBinder
  {
    private readonly Func<PendingOperation, Task<ALKScriptValue>> _start;

    internal FuncBinder(Func<PendingOperation, Task<ALKScriptValue>> start) => _start = start;

    public Task<ALKScriptValue> Start(PendingOperation operation) => _start(operation);

    public void Discard(PendingOperation operation, Action<Exception> onFault)
    {
      _ = _start(operation);
    }

    public void OnOperationFaulted(PendingOperation operation, Exception fault)
    {
    }
  }
}
