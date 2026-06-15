using System;
using System.Collections.Generic;
using ALKScript.Interpreter.Common.Evaluation.Scheduling;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Evaluator.Cursor;

namespace Tests.ALKScript.Interpreter.Evaluator.Cursor;

/// <summary>
/// Step 12 coverage for the cursor-rewrite plan (docs:
/// validated-nibbling-narwhal): "Phase A" (replay-based)
/// <see cref="CursorProgramEvaluator.Capture"/>/<see cref="CursorProgramEvaluator.Restore"/>
/// (docs/ASYNC_AWAIT_DESIGN.md Addendum 3) — capturing a suspended run's
/// record-and-replay log and reconstructing an equivalent suspended run from
/// it against a fresh evaluator/bindings.
/// </summary>
public class CursorCaptureRestoreTests : EvaluatorTestBase
{
  [Fact]
  public void CaptureRestore_SingleAwaitSuspension_RestoresAndResumesToCompletion()
  {
    var source = $"{RecordDeclaration}\nnative function thunk<int> fetch();\nvar x = await fetch();\nrecord(x);";

    var bindings = new ScriptNativeBindings
    {
      ["record"] = arguments => NullValue.Instance
    };
    var binder = new LiveBinder();

    var graph = LoadGraph(source);
    var evaluator = new CursorProgramEvaluator(bindings, operationBinder: binder);

    Assert.Equal(ProgramRunResult.Awaiting, evaluator.Evaluate(graph));

    var state = evaluator.Capture();
    Assert.Empty(state.Log);

    var recorded = new List<ALKScriptValue>();
    var bindings2 = new ScriptNativeBindings
    {
      ["record"] = arguments => { recorded.Add(arguments[0]); return NullValue.Instance; }
    };
    var binder2 = new LiveBinder();

    var restored = CursorProgramEvaluator.Restore(graph, state, out var restoreResult, bindings2, operationBinder: binder2);

    Assert.Equal(ProgramRunResult.Awaiting, restoreResult);
    Assert.Empty(recorded);

    var result = restored.Resume(new IntValue(7));

    Assert.Equal(ProgramRunResult.Completed, result);
    Assert.Equal(7L, Assert.IsType<IntValue>(recorded[0]).Value);
  }

  [Fact]
  public void CaptureRestore_CompositeWhenAllAlreadyResolved_ReplaysWithoutSuspendingThenSuspendsAtNextAwait()
  {
    var source = $"{RecordDeclaration}\nnative function thunk<int> a();\nnative function thunk<int> b();\nnative function thunk<int> c();\nvar r = await [a(), b()];\nvar y = await c();\nrecord(r[0] + r[1] + y);";

    var bindings = new ScriptNativeBindings
    {
      ["record"] = arguments => NullValue.Instance
    };
    var binder = new LiveBinder(onStart: op => op.Name switch
    {
      "a" => new OperationStatus.Resolved(new IntValue(1)),
      "b" => new OperationStatus.Resolved(new IntValue(2)),
      _ => OperationStatus.Pending.Instance,
    });

    var graph = LoadGraph(source);
    var evaluator = new CursorProgramEvaluator(bindings, operationBinder: binder);

    Assert.Equal(ProgramRunResult.Awaiting, evaluator.Evaluate(graph));

    var state = evaluator.Capture();
    Assert.Equal(2, state.Log.Count);

    var recorded = new List<ALKScriptValue>();
    var bindings2 = new ScriptNativeBindings
    {
      ["record"] = arguments => { recorded.Add(arguments[0]); return NullValue.Instance; }
    };
    var binder2 = new LiveBinder(onStart: op => op.Name switch
    {
      "a" => new OperationStatus.Resolved(new IntValue(1)),
      "b" => new OperationStatus.Resolved(new IntValue(2)),
      _ => OperationStatus.Pending.Instance,
    });

    var restored = CursorProgramEvaluator.Restore(graph, state, out var restoreResult, bindings2, operationBinder: binder2);

    Assert.Equal(ProgramRunResult.Awaiting, restoreResult);
    Assert.NotNull(restored.PendingAwait);
    Assert.Null(restored.PendingAwait!.CompositeElements);

    var result = restored.Resume(new IntValue(3));

    Assert.Equal(ProgramRunResult.Completed, result);
    Assert.Equal(6L, Assert.IsType<IntValue>(recorded[0]).Value);
  }

  [Fact]
  public void CaptureRestore_AfterSecondOfThreeAwaits_ReplaysFirstAndSuspendsAtSecondNotThird()
  {
    var source = $"{RecordDeclaration}\nnative function thunk<int> fetch1();\nnative function thunk<int> fetch2();\nnative function thunk<int> fetch3();\nvar a = await fetch1();\nvar b = await fetch2();\nvar c = await fetch3();\nrecord(a);\nrecord(b);\nrecord(c);";

    var bindings = new ScriptNativeBindings
    {
      ["record"] = arguments => NullValue.Instance
    };
    var binder = new LiveBinder();

    var graph = LoadGraph(source);
    var evaluator = new CursorProgramEvaluator(bindings, operationBinder: binder);

    Assert.Equal(ProgramRunResult.Awaiting, evaluator.Evaluate(graph));
    Assert.Equal(ProgramRunResult.Awaiting, evaluator.Resume(new IntValue(1)));

    var state = evaluator.Capture();
    Assert.Single(state.Log);
    Assert.Equal("fetch1", state.Log[0].Operation.Name);

    var recorded = new List<ALKScriptValue>();
    var bindings2 = new ScriptNativeBindings
    {
      ["record"] = arguments => { recorded.Add(arguments[0]); return NullValue.Instance; }
    };
    var binder2 = new LiveBinder();

    var restored = CursorProgramEvaluator.Restore(graph, state, out var restoreResult, bindings2, operationBinder: binder2);

    Assert.Equal(ProgramRunResult.Awaiting, restoreResult);
    Assert.Single(restored.Log);

    Assert.Equal(ProgramRunResult.Awaiting, restored.Resume(new IntValue(2)));
    Assert.Equal(ProgramRunResult.Completed, restored.Resume(new IntValue(3)));

    Assert.Equal(1L, Assert.IsType<IntValue>(recorded[0]).Value);
    Assert.Equal(2L, Assert.IsType<IntValue>(recorded[1]).Value);
    Assert.Equal(3L, Assert.IsType<IntValue>(recorded[2]).Value);
  }

  [Fact]
  public void Capture_WhileNotAwaiting_Throws()
  {
    var source = $"{RecordDeclaration}\nrecord(1);";

    var bindings = new ScriptNativeBindings
    {
      ["record"] = arguments => NullValue.Instance
    };

    var graph = LoadGraph(source);
    var evaluator = new CursorProgramEvaluator(bindings);

    Assert.Equal(ProgramRunResult.Completed, evaluator.Evaluate(graph));
    Assert.Throws<InvalidOperationException>(() => evaluator.Capture());
  }

  /// <summary>
  /// A binder whose <see cref="Start"/> reports <see cref="OperationStatus.Pending"/>
  /// (or, via <paramref name="onStart"/>, a test-supplied status per operation)
  /// for every operation; <see cref="Poll"/> re-checks a settlement map that
  /// <see cref="Settle"/> mutates from the test.
  /// </summary>
  private sealed class LiveBinder : IAsyncOperationBinder
  {
    private readonly Func<PendingOperation, OperationStatus>? _onStart;
    private readonly Dictionary<string, OperationStatus> _settled = new();

    internal LiveBinder(Func<PendingOperation, OperationStatus>? onStart = null) => _onStart = onStart;

    internal void Settle(string operationName, ALKScriptValue value) => _settled[operationName] = new OperationStatus.Resolved(value);

    public OperationStatus Start(PendingOperation operation) =>
      _onStart?.Invoke(operation) ?? OperationStatus.Pending.Instance;

    public OperationStatus Poll(PendingOperation operation) =>
      _settled.TryGetValue(operation.Name, out var status) ? status : OperationStatus.Pending.Instance;

    public void Discard(PendingOperation operation, Action<Exception> onFault) { }

    public void OnOperationFaulted(PendingOperation operation, Exception fault)
    {
    }
  }
}
