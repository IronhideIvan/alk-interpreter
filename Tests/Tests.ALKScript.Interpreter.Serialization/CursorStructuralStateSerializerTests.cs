using System;
using System.Collections.Generic;
using System.Linq;
using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Common.Evaluation.Scheduling;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Common.Modules;
using ALKScript.Interpreter.Evaluator;
using ALKScript.Interpreter.Evaluator.Cursor;
using ALKScript.Interpreter.Lexer;
using ALKScript.Interpreter.Parser;
using ALKScript.Interpreter.Serialization;

namespace Tests.ALKScript.Interpreter.Serialization;

/// <summary>
/// Step 15 coverage for the structural-snapshot Capture/Restore plan (docs:
/// validated-nibbling-narwhal): <see cref="CursorStructuralStateSerializer"/>'s
/// end-to-end JSON byte-stream round trip (the "Phase B" Capture/Restore
/// design, docs/ASYNC_AWAIT_DESIGN.md Addendum 3), combining cyclic
/// instances, a bound method value, a generic instance, and a composite
/// <c>await [a, b]</c> suspension in a single run.
/// </summary>
public class CursorStructuralStateSerializerTests
{
  private const string RecordDeclaration = "native function void record(Object value);\n";

  [Fact]
  public void CaptureAndRestore_CyclicInstancesGenericBoxAndCompositeAwait_JsonRoundTripRestoresAndResumes()
  {
    var source = $"{RecordDeclaration}native function thunk<int> fetch();\n" +
      "class Node {\n" +
      "  public Node next;\n" +
      "  public int value;\n" +
      "  public function int describe() {\n" +
      "    return this.value + this.next.value;\n" +
      "  }\n" +
      "}\n" +
      "class Box<T> {\n" +
      "  public T value;\n" +
      "  public new(T value) {\n" +
      "    this.value = value;\n" +
      "  }\n" +
      "}\n" +
      "var a = new Node();\n" +
      "var b = new Node();\n" +
      "a.next = b;\n" +
      "b.next = a;\n" +
      "a.value = 1;\n" +
      "b.value = 2;\n" +
      "var box = new Box<int>(40);\n" +
      "var m = a.describe;\n" +
      "for (var i = 0; i < 1; i = i + 1) {\n" +
      "  var r = await [fetch(), 2];\n" +
      "  record(m() + box.value + r[0] + r[1]);\n" +
      "}";

    var graph = LoadGraph(source);

    var bindings = new ScriptNativeBindings { ["record"] = arguments => NullValue.Instance };
    var binder = new LiveBinder();

    var evaluator = new CursorProgramEvaluator(bindings, operationBinder: binder);
    Assert.Equal(ProgramRunResult.Awaiting, evaluator.Evaluate(graph));

    var bytes = CursorStructuralStateSerializer.Capture(evaluator);

    var recorded = new List<ALKScriptValue>();
    var bindings2 = new ScriptNativeBindings
    {
      ["record"] = arguments => { recorded.Add(arguments[0]); return NullValue.Instance; }
    };

    var binder2 = new LiveBinder();

    var restored = CursorStructuralStateSerializer.Restore(graph, bytes, out var restoreResult, bindings2, operationBinder: binder2);

    Assert.Equal(ProgramRunResult.Awaiting, restoreResult);
    Assert.Empty(recorded);

    binder2.Settle("fetch", new IntValue(8));
    PollPendingElements(restored);

    var result = restored.Resume(NullValue.Instance);

    Assert.Equal(ProgramRunResult.Completed, result);
    // m() == a.describe() == a.value + b.value == 1 + 2 == 3
    // box.value == 40, r[0] == 8 (reissued fetch), r[1] == 2
    Assert.Equal(53L, Assert.IsType<IntValue>(recorded[0]).Value);
  }

  [Fact]
  public void CaptureAndRestore_NotYetStartedPendingOperationLocal_JsonRoundTripRestoresAndStartsOnLaterAwait()
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
    var binder = new LiveBinder();

    var evaluator = new CursorProgramEvaluator(bindings, operationBinder: binder);
    Assert.Equal(ProgramRunResult.Awaiting, evaluator.Evaluate(graph));

    var bytes = CursorStructuralStateSerializer.Capture(evaluator);

    var recorded = new List<ALKScriptValue>();
    var bindings2 = new ScriptNativeBindings
    {
      ["record"] = arguments => { recorded.Add(arguments[0]); return NullValue.Instance; }
    };

    int startCount = 0;
    var binder2 = new LiveBinder(onStart: _ =>
    {
      startCount++;
      return OperationStatus.Pending.Instance;
    });

    var restored = CursorStructuralStateSerializer.Restore(graph, bytes, out var restoreResult, bindings2, operationBinder: binder2);

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
  public void CaptureAndRestore_CompositeAwaitElementAliasedWithLocal_JsonRoundTripReissuesOperationExactlyOnce()
  {
    var source = $"{RecordDeclaration}native function thunk<int> fetch();\n" +
      "for (var i = 0; i < 1; i = i + 1) {\n" +
      "  var op = fetch();\n" +
      "  var r = await [op, 5];\n" +
      "  record(r[0] + r[1]);\n" +
      "}";

    var graph = LoadGraph(source);

    var bindings = new ScriptNativeBindings { ["record"] = arguments => NullValue.Instance };
    var binder = new LiveBinder();

    var evaluator = new CursorProgramEvaluator(bindings, operationBinder: binder);
    Assert.Equal(ProgramRunResult.Awaiting, evaluator.Evaluate(graph));

    var bytes = CursorStructuralStateSerializer.Capture(evaluator);

    var recorded = new List<ALKScriptValue>();
    var bindings2 = new ScriptNativeBindings
    {
      ["record"] = arguments => { recorded.Add(arguments[0]); return NullValue.Instance; }
    };

    int startCount = 0;
    var binder2 = new LiveBinder(onStart: _ =>
    {
      startCount++;
      return OperationStatus.Pending.Instance;
    });

    var restored = CursorStructuralStateSerializer.Restore(graph, bytes, out var restoreResult, bindings2, operationBinder: binder2);

    Assert.Equal(ProgramRunResult.Awaiting, restoreResult);
    Assert.Equal(1, startCount); // op and the composite element are the same instance — started exactly once
    Assert.Empty(recorded);

    binder2.Settle("fetch", new IntValue(9));
    PollPendingElements(restored);

    var result = restored.Resume(NullValue.Instance);

    Assert.Equal(ProgramRunResult.Completed, result);
    Assert.Equal(14L, Assert.IsType<IntValue>(recorded[0]).Value); // 9 + 5
  }

  [Fact]
  public void CaptureAndRestore_LocalBoundToNativeMethodValue_JsonRoundTripCallsIt()
  {
    var source = $"{RecordDeclaration}native function thunk<int> fetch();\n" +
      "native class Doubler {\n" +
      "  public int factor;\n" +
      "  public native function int double(int x);\n" +
      "}\n" +
      "var d = new Doubler();\n" +
      "d.factor = 2;\n" +
      "for (var i = 0; i < 1; i = i + 1) {\n" +
      "  var f = d.double;\n" +
      "  var fetched = await fetch();\n" +
      "  record(f(fetched));\n" +
      "}";

    var graph = LoadGraph(source);

    var bindings = new ScriptNativeBindings { ["record"] = arguments => NullValue.Instance };
    var methodBindings = new ScriptNativeMethodBindings
    {
      ["Doubler", "double"] = (instance, arguments) =>
        new IntValue(((IntValue)instance.Fields["factor"]).Value * ((IntValue)arguments[0]).Value),
    };
    var binder = new LiveBinder();

    var evaluator = new CursorProgramEvaluator(bindings, methodBindings, operationBinder: binder);
    Assert.Equal(ProgramRunResult.Awaiting, evaluator.Evaluate(graph));

    var bytes = CursorStructuralStateSerializer.Capture(evaluator);

    var recorded = new List<ALKScriptValue>();
    var bindings2 = new ScriptNativeBindings
    {
      ["record"] = arguments => { recorded.Add(arguments[0]); return NullValue.Instance; }
    };
    var binder2 = new LiveBinder();

    var restored = CursorStructuralStateSerializer.Restore(graph, bytes, out var restoreResult, bindings2, methodBindings, operationBinder: binder2);

    Assert.Equal(ProgramRunResult.Awaiting, restoreResult);
    Assert.Empty(recorded);

    var result = restored.Resume(new IntValue(8));

    Assert.Equal(ProgramRunResult.Completed, result);
    Assert.Equal(16L, Assert.IsType<IntValue>(recorded[0]).Value);
  }

  private static ModuleGraph LoadGraph(string source, IReadOnlyList<string>? globalPreludeSources = null)
  {
    var program = Parse(source);
    var module = new LoadedModule("entry", ModuleKind.File, program);
    var preludes = (globalPreludeSources ?? Array.Empty<string>())
      .Select(Parse)
      .ToList();

    return new ModuleGraph(module, new Dictionary<string, LoadedModule> { ["entry"] = module }, preludes);
  }

  private static ProgramNode Parse(string source)
  {
    var lexer = new ALKScriptLexer();
    var tokens = lexer.Tokenize(source);
    var parser = new ALKScriptParser();

    return parser.ParseTokens(tokens);
  }

  /// <summary>
  /// Polls every composite element's <see cref="PendingOperationValue"/> (if
  /// any) so that operations the test settled via <see cref="LiveBinder.Settle"/>
  /// after suspension have their <see cref="PendingOperationValue.Status"/>
  /// refreshed before <see cref="CursorProgramEvaluator.Resume"/> calls
  /// <c>ResolveWhenAll</c> — mirroring what a host's <see cref="ProgramRun.Pump"/>
  /// would do.
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
  /// A binder whose <see cref="Start"/> reports <see cref="OperationStatus.Pending"/>
  /// (or, via <paramref name="onStart"/>, a test-supplied status while still
  /// recording the call) for every operation; <see cref="Poll"/> re-checks a
  /// settlement map that <see cref="Settle"/> mutates from the test.
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
