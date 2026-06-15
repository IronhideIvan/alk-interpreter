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
/// Step 12 coverage for the cursor-rewrite plan (docs:
/// validated-nibbling-narwhal): <see cref="CursorStateSerializer"/>'s
/// JSON byte-stream Capture/Restore round trip (the "Phase A" Capture/Restore
/// design, docs/ASYNC_AWAIT_DESIGN.md Addendum 3) and the
/// <see cref="NotSupportedException"/> raised for a non-primitive logged
/// value.
/// </summary>
public class CursorStateSerializerTests
{
  private const string RecordDeclaration = "native function void record(Object value);\n";

  [Fact]
  public void CaptureAndRestore_SingleAwaitSuspension_RestoresAndResumesToCompletion()
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

    var bytes = CursorStateSerializer.Capture(evaluator);

    var recorded = new List<ALKScriptValue>();
    var bindings2 = new ScriptNativeBindings
    {
      ["record"] = arguments => { recorded.Add(arguments[0]); return NullValue.Instance; }
    };
    var binder2 = new LiveBinder();

    var restored = CursorStateSerializer.Restore(graph, bytes, out var restoreResult, bindings2, operationBinder: binder2);

    Assert.Equal(ProgramRunResult.Awaiting, restoreResult);

    var result = restored.Resume(new IntValue(7));

    Assert.Equal(ProgramRunResult.Completed, result);
    Assert.Equal(7L, Assert.IsType<IntValue>(recorded[0]).Value);
  }

  [Fact]
  public void Capture_AfterAwaitResolvesToNonPrimitiveValue_ThrowsNotSupported()
  {
    var source = $"{RecordDeclaration}\nnative function thunk<int> fetch1();\nnative function thunk fetch2();\nnative function thunk<int> fetch3();\nvar a = await fetch1();\nvar b = await fetch2();\nvar c = await fetch3();\nrecord(b);";

    var bindings = new ScriptNativeBindings
    {
      ["record"] = arguments => NullValue.Instance
    };
    var binder = new LiveBinder();

    var graph = LoadGraph(source);
    var evaluator = new CursorProgramEvaluator(bindings, operationBinder: binder);

    Assert.Equal(ProgramRunResult.Awaiting, evaluator.Evaluate(graph));
    Assert.Equal(ProgramRunResult.Awaiting, evaluator.Resume(new IntValue(1)));
    Assert.Equal(ProgramRunResult.Awaiting, evaluator.Resume(new EnumValue("Color", "Red", 0)));

    Assert.Throws<NotSupportedException>(() => CursorStateSerializer.Capture(evaluator));
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
  /// A binder whose <see cref="Start"/> always reports <see cref="OperationStatus.Pending"/> —
  /// the test resumes directly via <c>evaluator.Resume(value)</c> without
  /// ever polling.
  /// </summary>
  private sealed class LiveBinder : IAsyncOperationBinder
  {
    public OperationStatus Start(PendingOperation operation) => OperationStatus.Pending.Instance;

    public OperationStatus Poll(PendingOperation operation) => OperationStatus.Pending.Instance;

    public void Discard(PendingOperation operation, Action<Exception> onFault) { }

    public void OnOperationFaulted(PendingOperation operation, Exception fault)
    {
    }
  }
}
