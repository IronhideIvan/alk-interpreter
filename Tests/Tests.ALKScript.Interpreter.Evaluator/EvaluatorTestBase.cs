using System.Collections.Generic;
using System.Linq;
using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Common.Evaluation.Scheduling;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Common.Modules;
using ALKScript.Interpreter.Lexer;
using ALKScript.Interpreter.Parser;
using ALKScript.Interpreter.Evaluator;
using ALKScript.Interpreter.Evaluator.Scheduling;

namespace Tests.ALKScript.Interpreter.Evaluator;

/// <summary>
/// Shared helper for running ALKScript source through the full
/// lex -&gt; parse -&gt; evaluate pipeline and observing the values it produces.
///
/// The evaluator runs a program purely for its side effects and returns
/// nothing, so tests observe results by declaring a host-bound
/// <c>record</c> native function (see <see cref="RecordDeclaration"/>) and
/// calling it with the value(s) under test; <see cref="Run"/> returns every
/// value passed to it, in call order.
/// </summary>
public abstract class EvaluatorTestBase
{
  /// <summary>
  /// A native declaration tests can call to observe a value. Its parameter
  /// type is nominal only — the evaluator performs no static type-checking,
  /// so any value may be passed regardless of the declared type.
  /// </summary>
  protected const string RecordDeclaration = "native function void record(Object value);\n";

  protected static IReadOnlyList<ALKScriptValue> Run(string source)
  {
    var recorded = new List<ALKScriptValue>();

    RunWithBindings(source, new ScriptNativeBindings
    {
      ["record"] = arguments =>
      {
        recorded.Add(arguments[0]);
        return NullValue.Instance;
      }
    });

    return recorded;
  }

  protected static void RunWithBindings(string source, ScriptNativeBindings nativeBindings)
  {
    var scheduler = new ScriptScheduler();
    var graph = LoadGraph(source);
    scheduler.RunUntilComplete(new ProgramEvaluator(nativeBindings, scheduler: scheduler).Evaluate(graph));
  }

  /// <summary>
  /// Like <see cref="RunWithBindings"/>, but supplies an
  /// <paramref name="operationBinder"/> for <c>async native</c> free-standing
  /// function declarations (see <see cref="IAsyncOperationBinder"/>).
  /// </summary>
  protected static void RunWithOperationBinder(string source, ScriptNativeBindings? nativeBindings, IAsyncOperationBinder operationBinder)
  {
    var scheduler = new ScriptScheduler();
    var graph = LoadGraph(source);
    scheduler.RunUntilComplete(new ProgramEvaluator(nativeBindings, operationBinder: operationBinder, scheduler: scheduler).Evaluate(graph));
  }

  /// <summary>
  /// Like <see cref="RunWithBindings"/>, but also injects
  /// <paramref name="nativeMethodBindings"/> for <c>native</c> methods.
  /// </summary>
  protected static void RunWithMethodBindings(string source, ScriptNativeBindings? nativeBindings, ScriptNativeMethodBindings nativeMethodBindings)
  {
    var scheduler = new ScriptScheduler();
    var graph = LoadGraph(source);
    scheduler.RunUntilComplete(new ProgramEvaluator(nativeBindings, nativeMethodBindings, scheduler: scheduler).Evaluate(graph));
  }

  /// <summary>
  /// Runs <paramref name="source"/> live (no replay log), returning both the
  /// values passed to <c>record()</c> and the full operation log captured
  /// during evaluation — for use in replay-round-trip tests.
  /// </summary>
  protected static (IReadOnlyList<ALKScriptValue> Recorded, IReadOnlyList<OperationLogEntry> Log)
    RunAndCaptureLog(string source, IAsyncOperationBinder operationBinder, ScriptNativeBindings? extraBindings = null)
  {
    var recorded = new List<ALKScriptValue>();
    var bindings = new ScriptNativeBindings(extraBindings ?? new ScriptNativeBindings())
    {
      ["record"] = arguments => { recorded.Add(arguments[0]); return NullValue.Instance; }
    };
    var scheduler = new ScriptScheduler();
    var graph = LoadGraph(source);
    var evaluator = new ProgramEvaluator(bindings, operationBinder: operationBinder, scheduler: scheduler);
    scheduler.RunUntilComplete(evaluator.Evaluate(graph));
    return (recorded, evaluator.Log);
  }

  /// <summary>
  /// Replays <paramref name="source"/> against <paramref name="replayLog"/>,
  /// returning the values passed to <c>record()</c>.
  /// </summary>
  protected static IReadOnlyList<ALKScriptValue> RunWithReplayLog(string source, IAsyncOperationBinder operationBinder, IReadOnlyList<OperationLogEntry> replayLog, ScriptNativeBindings? extraBindings = null)
  {
    var recorded = new List<ALKScriptValue>();
    var bindings = new ScriptNativeBindings(extraBindings ?? new ScriptNativeBindings())
    {
      ["record"] = arguments => { recorded.Add(arguments[0]); return NullValue.Instance; }
    };
    var scheduler = new ScriptScheduler();
    var graph = LoadGraph(source);
    var evaluator = new ProgramEvaluator(bindings, operationBinder: operationBinder, replayLog: replayLog, scheduler: scheduler);
    scheduler.RunUntilComplete(evaluator.Evaluate(graph));
    return recorded;
  }

  /// <summary>
  /// Like <see cref="RunWithBindings"/>, but seeds the graph's global preludes.
  /// </summary>
  protected static void RunWithGlobals(string source, IReadOnlyList<string> globalPreludeSources, ScriptNativeBindings nativeBindings)
  {
    var scheduler = new ScriptScheduler();
    var graph = LoadGraph(source, globalPreludeSources);
    scheduler.RunUntilComplete(new ProgramEvaluator(nativeBindings, scheduler: scheduler).Evaluate(graph));
  }

  protected static ModuleGraph LoadGraph(string source, IReadOnlyList<string>? globalPreludeSources = null)
  {
    var program = Parse(source);
    var module = new LoadedModule("entry", ModuleKind.File, program);
    var preludes = (globalPreludeSources ?? System.Array.Empty<string>())
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
}
