using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Common.Modules;
using ALKScript.Interpreter.Lexer;
using ALKScript.Interpreter.Parser;
using ALKScript.Interpreter.Evaluator;

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
    new ProgramEvaluator(new ALKScriptLexer(), new ALKScriptParser(), nativeBindings).Evaluate(graph);
  }

  protected static ModuleGraph LoadGraph(string source)
  {
    var program = Parse(source);
    var module = new LoadedModule("entry", ModuleKind.File, program);

    return new ModuleGraph(module, new Dictionary<string, LoadedModule> { ["entry"] = module });
  }

  private static ProgramNode Parse(string source)
  {
    var lexer = new ALKScriptLexer();
    var tokens = lexer.Tokenize(source);
    var parser = new ALKScriptParser();

    return parser.ParseTokens(tokens);
  }
}
