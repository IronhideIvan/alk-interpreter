using ALKScript.Interpreter.Common;
using ALKScript.Interpreter.Common.Evaluation;
using ALKScript.Interpreter.Common.Evaluation.Scheduling;
using ALKScript.Interpreter.Common.Modules;

namespace ALKScript.Interpreter.Runtime
{
  /// <summary>
  /// The primary host-facing entry point for running ALKScript programs.
  /// Wires together the lexer, parser, loader, and evaluator and exposes the
  /// two run methods declared on <see cref="IProgramRuntime"/>.
  /// </summary>
  public class ProgramRuntime : IProgramRuntime
  {
    private readonly IProgramLoader _loader;
    private readonly IEvaluator _evaluator;

    public ProgramRuntime(IProgramLoader loader, IEvaluator evaluator)
    {
      _loader = loader;
      _evaluator = evaluator;
    }

    /// <inheritdoc/>
    public ScriptEvaluation RunFromSource(string source)
    {
      var graph = _loader.LoadFromSource(source);
      return _evaluator.Evaluate(graph);
    }

    /// <inheritdoc/>
    public ScriptEvaluation RunFromFile(string filePath)
    {
      var graph = _loader.Load(filePath);
      return _evaluator.Evaluate(graph);
    }
  }
}
