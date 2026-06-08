using System.Linq;
using ALKScript.Interpreter.Common.Ast;
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
    var graph = LoadGraph(source);

    // The evaluator is Task-returning so "await" can suspend mid-script (see
    // IEvaluationContext) — and, with real suspension (see TaskValue/EvalAwait),
    // some of these tests' scripts genuinely do suspend and later resume via
    // continuations that must run on the scheduler's single host thread (see
    // ScriptScheduler — design decision #2). Driving evaluation through
    // RunUntilComplete is the test-helper equivalent of a host's per-tick
    // Pump() loop: it installs the scheduler as SynchronizationContext.Current
    // and pumps continuations until the whole script — including any
    // suspensions — has completed, then unwraps the result/exception.
    new ScriptScheduler().RunUntilComplete(new ProgramEvaluator(nativeBindings).Evaluate(graph));
  }

  /// <summary>
  /// Like <see cref="RunWithBindings"/>, but also injects
  /// <paramref name="nativeMethodBindings"/> — host implementations for
  /// <c>native</c> methods, keyed by declaring class and member name (see
  /// <see cref="ScriptNativeMethodBindings"/> and <see cref="ProgramEvaluator"/>'s
  /// constructor docs) — so tests can exercise native methods that read or
  /// mutate the receiving instance's state.
  /// </summary>
  protected static void RunWithMethodBindings(string source, ScriptNativeBindings? nativeBindings, ScriptNativeMethodBindings nativeMethodBindings)
  {
    var graph = LoadGraph(source);
    new ScriptScheduler().RunUntilComplete(new ProgramEvaluator(nativeBindings, nativeMethodBindings).Evaluate(graph));
  }

  /// <summary>
  /// Like <see cref="RunWithBindings"/>, but also seeds the graph's
  /// <see cref="ModuleGraph.GlobalPreludes"/> from <paramref name="globalPreludeSources"/>
  /// — ALKScript source(s) compiled and executed into the root environment
  /// before <paramref name="source"/> runs, the same way <c>IProgramLoader</c>
  /// would compile a runtime-supplied <c>IGlobalPreludeProvider</c> (see
  /// <see cref="ProgramEvaluator"/>'s constructor docs and
  /// <see cref="ModuleGraph.GlobalPreludes"/>).
  /// </summary>
  protected static void RunWithGlobals(string source, IReadOnlyList<string> globalPreludeSources, ScriptNativeBindings nativeBindings)
  {
    var graph = LoadGraph(source, globalPreludeSources);
    new ScriptScheduler().RunUntilComplete(new ProgramEvaluator(nativeBindings).Evaluate(graph));
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
