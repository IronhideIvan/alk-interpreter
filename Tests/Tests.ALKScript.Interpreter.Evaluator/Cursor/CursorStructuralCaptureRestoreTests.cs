using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ALKScript.Interpreter.Common.Evaluation.Scheduling;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Evaluator;
using ALKScript.Interpreter.Evaluator.Cursor;

namespace Tests.ALKScript.Interpreter.Evaluator.Cursor;

/// <summary>
/// Steps 6-7 coverage for the cursor-rewrite plan (docs:
/// validated-nibbling-narwhal): "Phase B" (structural-snapshot)
/// <see cref="CursorProgramEvaluator.CaptureStructural"/>/<see cref="CursorProgramEvaluator.RestoreStructural"/>
/// (docs/ASYNC_AWAIT_DESIGN.md Addendum 3) for a primitives-only suspended
/// trail/environment graph with a single not-yet-started <c>thunk</c>
/// operation pending.
/// </summary>
public class CursorStructuralCaptureRestoreTests : EvaluatorTestBase
{
  [Fact]
  public void CaptureRestore_SingleAwaitInsideForLoop_RestoresPrimitiveLocalsAndResumesToCompletion()
  {
    var source = $"{RecordDeclaration}native function thunk<int> fetch();\n" +
      "var total = 0;\n" +
      "for (var i = 0; i < 1; i = i + 1) {\n" +
      "  var doubled = i * 2;\n" +
      "  var fetched = await fetch();\n" +
      "  total = fetched + doubled;\n" +
      "}\n" +
      "record(total);";

    var graph = LoadGraph(source);

    var bindings = new ScriptNativeBindings
    {
      ["record"] = arguments => NullValue.Instance
    };
    var binder = new FuncBinder(_ => new TaskCompletionSource<ALKScriptValue>().Task);

    var evaluator = new CursorProgramEvaluator(bindings, operationBinder: binder);
    Assert.Equal(ProgramRunResult.Awaiting, evaluator.Evaluate(graph));

    var state = evaluator.CaptureStructural();

    var recorded = new List<ALKScriptValue>();
    var bindings2 = new ScriptNativeBindings
    {
      ["record"] = arguments => { recorded.Add(arguments[0]); return NullValue.Instance; }
    };
    var binder2 = new FuncBinder(_ => new TaskCompletionSource<ALKScriptValue>().Task);

    var restored = CursorProgramEvaluator.RestoreStructural(graph, state, out var restoreResult, bindings2, operationBinder: binder2);

    Assert.Equal(ProgramRunResult.Awaiting, restoreResult);
    Assert.Empty(recorded);

    var result = restored.Resume(new IntValue(5));

    Assert.Equal(ProgramRunResult.Completed, result);
    Assert.Equal(5L, Assert.IsType<IntValue>(recorded[0]).Value);
  }

  [Fact]
  public void CaptureRestore_LocalBoundToTopLevelFunctionValue_RestoresAndCallsIt()
  {
    var source = $"{RecordDeclaration}native function thunk<int> fetch();\n" +
      "function int square(int x) {\n" +
      "  return x * x;\n" +
      "}\n" +
      "var f = square;\n" +
      "for (var i = 0; i < 1; i = i + 1) {\n" +
      "  var fetched = await fetch();\n" +
      "  record(f(fetched));\n" +
      "}";

    var graph = LoadGraph(source);

    var bindings = new ScriptNativeBindings
    {
      ["record"] = arguments => NullValue.Instance
    };
    var binder = new FuncBinder(_ => new TaskCompletionSource<ALKScriptValue>().Task);

    var evaluator = new CursorProgramEvaluator(bindings, operationBinder: binder);
    Assert.Equal(ProgramRunResult.Awaiting, evaluator.Evaluate(graph));

    var state = evaluator.CaptureStructural();

    var recorded = new List<ALKScriptValue>();
    var bindings2 = new ScriptNativeBindings
    {
      ["record"] = arguments => { recorded.Add(arguments[0]); return NullValue.Instance; }
    };
    var binder2 = new FuncBinder(_ => new TaskCompletionSource<ALKScriptValue>().Task);

    var restored = CursorProgramEvaluator.RestoreStructural(graph, state, out var restoreResult, bindings2, operationBinder: binder2);

    Assert.Equal(ProgramRunResult.Awaiting, restoreResult);
    Assert.Empty(recorded);

    var result = restored.Resume(new IntValue(6));

    Assert.Equal(ProgramRunResult.Completed, result);
    Assert.Equal(36L, Assert.IsType<IntValue>(recorded[0]).Value);
  }

  [Fact]
  public void CaptureRestore_CyclicInstancesAcrossAwait_PreserveReferenceIdentityAndMethodDispatch()
  {
    var source = $"{RecordDeclaration}native function thunk<int> fetch();\n" +
      "class Node {\n" +
      "  public Node next;\n" +
      "  public int value;\n" +
      "  public function int describe() {\n" +
      "    return this.value + this.next.value;\n" +
      "  }\n" +
      "}\n" +
      "var a = new Node();\n" +
      "var b = new Node();\n" +
      "a.next = b;\n" +
      "b.next = a;\n" +
      "a.value = 1;\n" +
      "b.value = 2;\n" +
      "for (var i = 0; i < 1; i = i + 1) {\n" +
      "  var fetched = await fetch();\n" +
      "  record(a.describe() + fetched);\n" +
      "  a.next.value = fetched;\n" +
      "  record(b.value);\n" +
      "}";

    var graph = LoadGraph(source);

    var bindings = new ScriptNativeBindings { ["record"] = arguments => NullValue.Instance };
    var binder = new FuncBinder(_ => new TaskCompletionSource<ALKScriptValue>().Task);

    var evaluator = new CursorProgramEvaluator(bindings, operationBinder: binder);
    Assert.Equal(ProgramRunResult.Awaiting, evaluator.Evaluate(graph));

    var state = evaluator.CaptureStructural();

    var recorded = new List<ALKScriptValue>();
    var bindings2 = new ScriptNativeBindings
    {
      ["record"] = arguments => { recorded.Add(arguments[0]); return NullValue.Instance; }
    };
    var binder2 = new FuncBinder(_ => new TaskCompletionSource<ALKScriptValue>().Task);

    var restored = CursorProgramEvaluator.RestoreStructural(graph, state, out var restoreResult, bindings2, operationBinder: binder2);

    Assert.Equal(ProgramRunResult.Awaiting, restoreResult);
    Assert.Empty(recorded);

    var result = restored.Resume(new IntValue(10));

    Assert.Equal(ProgramRunResult.Completed, result);
    Assert.Equal(13L, Assert.IsType<IntValue>(recorded[0]).Value);
    Assert.Equal(10L, Assert.IsType<IntValue>(recorded[1]).Value);
  }

  [Fact]
  public void CaptureRestore_StaticFieldMutatedByMultipleInstances_RestoresSharedCounter()
  {
    var source = $"{RecordDeclaration}native function thunk<int> fetch();\n" +
      "class Counter {\n" +
      "  public static int instances = 0;\n" +
      "  new() {\n" +
      "    Counter.instances = Counter.instances + 1;\n" +
      "  }\n" +
      "}\n" +
      "var a = new Counter();\n" +
      "var b = new Counter();\n" +
      "for (var i = 0; i < 1; i = i + 1) {\n" +
      "  var fetched = await fetch();\n" +
      "  var c = new Counter();\n" +
      "  record(Counter.instances + fetched);\n" +
      "}";

    var graph = LoadGraph(source);

    var bindings = new ScriptNativeBindings { ["record"] = arguments => NullValue.Instance };
    var binder = new FuncBinder(_ => new TaskCompletionSource<ALKScriptValue>().Task);

    var evaluator = new CursorProgramEvaluator(bindings, operationBinder: binder);
    Assert.Equal(ProgramRunResult.Awaiting, evaluator.Evaluate(graph));

    var state = evaluator.CaptureStructural();

    var recorded = new List<ALKScriptValue>();
    var bindings2 = new ScriptNativeBindings
    {
      ["record"] = arguments => { recorded.Add(arguments[0]); return NullValue.Instance; }
    };
    var binder2 = new FuncBinder(_ => new TaskCompletionSource<ALKScriptValue>().Task);

    var restored = CursorProgramEvaluator.RestoreStructural(graph, state, out var restoreResult, bindings2, operationBinder: binder2);

    Assert.Equal(ProgramRunResult.Awaiting, restoreResult);
    Assert.Empty(recorded);

    var result = restored.Resume(new IntValue(100));

    Assert.Equal(ProgramRunResult.Completed, result);
    Assert.Equal(103L, Assert.IsType<IntValue>(recorded[0]).Value);
  }

  [Fact]
  public void CaptureRestore_LocalBoundToBoundMethodValue_RestoresAndCallsIt()
  {
    var source = $"{RecordDeclaration}native function thunk<int> fetch();\n" +
      "class Greeter {\n" +
      "  public int value;\n" +
      "  public function int describe() {\n" +
      "    return this.value * 2;\n" +
      "  }\n" +
      "}\n" +
      "var g = new Greeter();\n" +
      "g.value = 21;\n" +
      "var m = g.describe;\n" +
      "for (var i = 0; i < 1; i = i + 1) {\n" +
      "  var fetched = await fetch();\n" +
      "  record(m() + fetched);\n" +
      "}";

    var graph = LoadGraph(source);

    var bindings = new ScriptNativeBindings { ["record"] = arguments => NullValue.Instance };
    var binder = new FuncBinder(_ => new TaskCompletionSource<ALKScriptValue>().Task);

    var evaluator = new CursorProgramEvaluator(bindings, operationBinder: binder);
    Assert.Equal(ProgramRunResult.Awaiting, evaluator.Evaluate(graph));

    var state = evaluator.CaptureStructural();

    var recorded = new List<ALKScriptValue>();
    var bindings2 = new ScriptNativeBindings
    {
      ["record"] = arguments => { recorded.Add(arguments[0]); return NullValue.Instance; }
    };
    var binder2 = new FuncBinder(_ => new TaskCompletionSource<ALKScriptValue>().Task);

    var restored = CursorProgramEvaluator.RestoreStructural(graph, state, out var restoreResult, bindings2, operationBinder: binder2);

    Assert.Equal(ProgramRunResult.Awaiting, restoreResult);
    Assert.Empty(recorded);

    var result = restored.Resume(new IntValue(1));

    Assert.Equal(ProgramRunResult.Completed, result);
    Assert.Equal(43L, Assert.IsType<IntValue>(recorded[0]).Value);
  }

  [Fact]
  public void CaptureRestore_CompositeAwaitWithResolvedAndPendingElements_RestoresAndResumesToCompletion()
  {
    var source = $"{RecordDeclaration}native function thunk<int> fetch();\n" +
      "for (var i = 0; i < 1; i = i + 1) {\n" +
      "  var r = await [fetch(), 5];\n" +
      "  record(r[0] + r[1]);\n" +
      "}";

    var graph = LoadGraph(source);

    var bindings = new ScriptNativeBindings { ["record"] = arguments => NullValue.Instance };
    var binder = new FuncBinder(_ => new TaskCompletionSource<ALKScriptValue>().Task);

    var evaluator = new CursorProgramEvaluator(bindings, operationBinder: binder);
    Assert.Equal(ProgramRunResult.Awaiting, evaluator.Evaluate(graph));

    var state = evaluator.CaptureStructural();

    var recorded = new List<ALKScriptValue>();
    var bindings2 = new ScriptNativeBindings
    {
      ["record"] = arguments => { recorded.Add(arguments[0]); return NullValue.Instance; }
    };

    TaskCompletionSource<ALKScriptValue>? tcs = null;
    var binder2 = new FuncBinder(_ =>
    {
      tcs = new TaskCompletionSource<ALKScriptValue>();
      return tcs.Task;
    });

    var restored = CursorProgramEvaluator.RestoreStructural(graph, state, out var restoreResult, bindings2, operationBinder: binder2);

    Assert.Equal(ProgramRunResult.Awaiting, restoreResult);
    Assert.NotNull(restored.PendingAwait);
    Assert.NotNull(restored.PendingAwait!.CompositeElements);
    Assert.Empty(recorded);

    Assert.NotNull(tcs);
    tcs!.SetResult(new IntValue(7));

    var result = restored.Resume(NullValue.Instance);

    Assert.Equal(ProgramRunResult.Completed, result);
    Assert.Equal(12L, Assert.IsType<IntValue>(recorded[0]).Value);
  }

  [Fact]
  public void CaptureRestore_NotYetStartedPendingOperationLocal_RestoresAndStartsOnLaterAwait()
  {
    var source = $"{RecordDeclaration}native function thunk<int> fetch();\n" +
      "native function thunk<int> probe();\n" +
      "for (var i = 0; i < 1; i = i + 1) {\n" +
      "  var op = probe();\n" +
      "  var x = await fetch();\n" +
      "  var opResult = await op;\n" +
      "  record(x + opResult);\n" +
      "}";

    var graph = LoadGraph(source);

    var bindings = new ScriptNativeBindings { ["record"] = arguments => NullValue.Instance };
    var binder = new FuncBinder(_ => new TaskCompletionSource<ALKScriptValue>().Task);

    var evaluator = new CursorProgramEvaluator(bindings, operationBinder: binder);
    Assert.Equal(ProgramRunResult.Awaiting, evaluator.Evaluate(graph));

    var state = evaluator.CaptureStructural();

    var recorded = new List<ALKScriptValue>();
    var bindings2 = new ScriptNativeBindings
    {
      ["record"] = arguments => { recorded.Add(arguments[0]); return NullValue.Instance; }
    };

    int startCount = 0;
    var binder2 = new FuncBinder(_ =>
    {
      startCount++;
      return new TaskCompletionSource<ALKScriptValue>().Task;
    });

    var restored = CursorProgramEvaluator.RestoreStructural(graph, state, out var restoreResult, bindings2, operationBinder: binder2);

    Assert.Equal(ProgramRunResult.Awaiting, restoreResult);
    Assert.Equal(1, startCount); // only the suspending await's own operand (fetch) is restarted on Restore
    Assert.Empty(recorded);

    var afterFetch = restored.Resume(new IntValue(5));

    Assert.Equal(ProgramRunResult.Awaiting, afterFetch); // now suspended on `await op`
    Assert.Equal(2, startCount); // `await op` triggered op.Start()

    var result = restored.Resume(new IntValue(7));

    Assert.Equal(ProgramRunResult.Completed, result);
    Assert.Equal(12L, Assert.IsType<IntValue>(recorded[0]).Value);
  }

  [Fact]
  public void CaptureRestore_LocalAliasedWithAwaitOperand_ReissuesOperationExactlyOnce()
  {
    var source = $"{RecordDeclaration}native function thunk<int> fetch();\n" +
      "for (var i = 0; i < 1; i = i + 1) {\n" +
      "  var op = fetch();\n" +
      "  var x = await op;\n" +
      "  record(x);\n" +
      "}";

    var graph = LoadGraph(source);

    var bindings = new ScriptNativeBindings { ["record"] = arguments => NullValue.Instance };
    var binder = new FuncBinder(_ => new TaskCompletionSource<ALKScriptValue>().Task);

    var evaluator = new CursorProgramEvaluator(bindings, operationBinder: binder);
    Assert.Equal(ProgramRunResult.Awaiting, evaluator.Evaluate(graph));

    var state = evaluator.CaptureStructural();

    var recorded = new List<ALKScriptValue>();
    var bindings2 = new ScriptNativeBindings
    {
      ["record"] = arguments => { recorded.Add(arguments[0]); return NullValue.Instance; }
    };

    int startCount = 0;
    var binder2 = new FuncBinder(_ =>
    {
      startCount++;
      return new TaskCompletionSource<ALKScriptValue>().Task;
    });

    var restored = CursorProgramEvaluator.RestoreStructural(graph, state, out var restoreResult, bindings2, operationBinder: binder2);

    Assert.Equal(ProgramRunResult.Awaiting, restoreResult);
    Assert.Equal(1, startCount); // op and the await's own operand are the same instance — started exactly once
    Assert.Empty(recorded);

    var result = restored.Resume(new IntValue(9));

    Assert.Equal(ProgramRunResult.Completed, result);
    Assert.Equal(9L, Assert.IsType<IntValue>(recorded[0]).Value);
    Assert.Equal(1, startCount); // resuming doesn't trigger a second start
  }

  [Fact]
  public void CaptureStructural_CompositeAwaitElementAliasedWithLocal_Throws()
  {
    var source = $"{RecordDeclaration}native function thunk<int> fetch();\n" +
      "for (var i = 0; i < 1; i = i + 1) {\n" +
      "  var op = fetch();\n" +
      "  var r = await [op, 5];\n" +
      "  record(r[0] + r[1]);\n" +
      "}";

    var graph = LoadGraph(source);

    var bindings = new ScriptNativeBindings { ["record"] = arguments => NullValue.Instance };
    var binder = new FuncBinder(_ => new TaskCompletionSource<ALKScriptValue>().Task);

    var evaluator = new CursorProgramEvaluator(bindings, operationBinder: binder);
    Assert.Equal(ProgramRunResult.Awaiting, evaluator.Evaluate(graph));

    var exception = Assert.Throws<NotSupportedException>(() => evaluator.CaptureStructural());
    Assert.Contains("composite", exception.Message);
  }

  [Fact]
  public void CaptureStructural_LocalBoundToNativeFunctionValue_Throws()
  {
    var source = $"{RecordDeclaration}native function thunk<int> fetch();\n" +
      "for (var i = 0; i < 1; i = i + 1) {\n" +
      "  var f = record;\n" +
      "  var fetched = await fetch();\n" +
      "}";

    var graph = LoadGraph(source);

    var bindings = new ScriptNativeBindings { ["record"] = arguments => NullValue.Instance };
    var binder = new FuncBinder(_ => new TaskCompletionSource<ALKScriptValue>().Task);

    var evaluator = new CursorProgramEvaluator(bindings, operationBinder: binder);
    Assert.Equal(ProgramRunResult.Awaiting, evaluator.Evaluate(graph));

    Assert.Throws<NotSupportedException>(() => evaluator.CaptureStructural());
  }

  [Fact]
  public void CaptureStructural_LocalBoundToLambdaValue_Throws()
  {
    var source = $"{RecordDeclaration}native function thunk<int> fetch();\n" +
      "for (var i = 0; i < 1; i = i + 1) {\n" +
      "  var f = int (int x) => { return x + 1; };\n" +
      "  var fetched = await fetch();\n" +
      "}";

    var graph = LoadGraph(source);

    var bindings = new ScriptNativeBindings { ["record"] = arguments => NullValue.Instance };
    var binder = new FuncBinder(_ => new TaskCompletionSource<ALKScriptValue>().Task);

    var evaluator = new CursorProgramEvaluator(bindings, operationBinder: binder);
    Assert.Equal(ProgramRunResult.Awaiting, evaluator.Evaluate(graph));

    Assert.Throws<NotSupportedException>(() => evaluator.CaptureStructural());
  }

  [Fact]
  public void CaptureStructural_ModuleWithDeclarationAfterTopLevelStatement_Throws()
  {
    var source = $"{RecordDeclaration}native function thunk<int> fetch();\n" +
      "for (var i = 0; i < 1; i = i + 1) {\n" +
      "  var fetched = await fetch();\n" +
      "}\n" +
      "function int square(int x) {\n" +
      "  return x * x;\n" +
      "}";

    var graph = LoadGraph(source);

    var bindings = new ScriptNativeBindings { ["record"] = arguments => NullValue.Instance };
    var binder = new FuncBinder(_ => new TaskCompletionSource<ALKScriptValue>().Task);

    var evaluator = new CursorProgramEvaluator(bindings, operationBinder: binder);
    Assert.Equal(ProgramRunResult.Awaiting, evaluator.Evaluate(graph));

    var exception = Assert.Throws<NotSupportedException>(() => evaluator.CaptureStructural());
    Assert.Contains("decls-before-statements", exception.Message);
  }

  [Fact]
  public void RestoreStructural_ThenDiscardWithoutResuming_DoesNotDoubleDiscardTheReissuedOperation()
  {
    var source = $"{RecordDeclaration}native function thunk<int> fetch();\n" +
      "for (var i = 0; i < 1; i = i + 1) {\n" +
      "  var fetched = await fetch();\n" +
      "  record(fetched);\n" +
      "}";

    var graph = LoadGraph(source);

    var bindings = new ScriptNativeBindings { ["record"] = arguments => NullValue.Instance };
    var binder = new FuncBinder(_ => new TaskCompletionSource<ALKScriptValue>().Task);

    var evaluator = new CursorProgramEvaluator(bindings, operationBinder: binder);
    Assert.Equal(ProgramRunResult.Awaiting, evaluator.Evaluate(graph));

    var state = evaluator.CaptureStructural();

    var bindings2 = new ScriptNativeBindings { ["record"] = arguments => NullValue.Instance };

    int discardCount = 0;
    var binder2 = new FuncBinder(_ =>
    {
      discardCount++;
      return new TaskCompletionSource<ALKScriptValue>().Task;
    });

    var factory2 = new FunctionValueFactory(bindings2, operationBinder: binder2);
    var restored = CursorProgramEvaluator.RestoreStructural(graph, factory2, state, out var restoreResult, operationBinder: binder2);

    Assert.Equal(ProgramRunResult.Awaiting, restoreResult);
    Assert.Equal(1, discardCount); // the reissue itself, via binder2.Start

    int faultCount = 0;
    factory2.DiscardPending(_ => faultCount++);

    Assert.Equal(0, faultCount);
    Assert.Equal(1, discardCount); // not double-discarded by DiscardPending
  }

  [Fact]
  public void CaptureStructural_WhileNotAwaiting_Throws()
  {
    var source = $"{RecordDeclaration}record(1);";

    var bindings = new ScriptNativeBindings
    {
      ["record"] = arguments => NullValue.Instance
    };

    var graph = LoadGraph(source);
    var evaluator = new CursorProgramEvaluator(bindings);

    Assert.Equal(ProgramRunResult.Completed, evaluator.Evaluate(graph));
    Assert.Throws<InvalidOperationException>(() => evaluator.CaptureStructural());
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
