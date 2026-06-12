using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
    var binder = new FuncBinder(_ => new TaskCompletionSource<ALKScriptValue>().Task);

    var evaluator = new CursorProgramEvaluator(bindings, operationBinder: binder);
    Assert.Equal(ProgramRunResult.Awaiting, evaluator.Evaluate(graph));

    var bytes = CursorStructuralStateSerializer.Capture(evaluator);

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

    var restored = CursorStructuralStateSerializer.Restore(graph, bytes, out var restoreResult, bindings2, operationBinder: binder2);

    Assert.Equal(ProgramRunResult.Awaiting, restoreResult);
    Assert.Empty(recorded);

    Assert.NotNull(tcs);
    tcs!.SetResult(new IntValue(8));

    var result = restored.Resume(NullValue.Instance);

    Assert.Equal(ProgramRunResult.Completed, result);
    // m() == a.describe() == a.value + b.value == 1 + 2 == 3
    // box.value == 40, r[0] == 8 (reissued fetch), r[1] == 2
    Assert.Equal(53L, Assert.IsType<IntValue>(recorded[0]).Value);
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
