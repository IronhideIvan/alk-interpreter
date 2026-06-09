using ALKScript.Interpreter.Common.Evaluation.Scheduling;
using ALKScript.Interpreter.Common.Modules;

namespace ALKScript.Interpreter.Common.Evaluation
{
  /// <summary>
  /// Executes a resolved module graph, running the entry module's top-level
  /// declarations and statements for their side effects.
  /// </summary>
  public interface IEvaluator
  {
    /// <summary>
    /// Starts evaluating <paramref name="graph"/> and returns an opaque
    /// <see cref="ScriptEvaluation"/> handle. Drive progress by calling
    /// <see cref="IScriptLoop.Pump"/> on each game-loop tick, or pass the
    /// handle to <see cref="IScriptLoop.RunUntilComplete"/> to block until
    /// the script finishes.
    /// </summary>
    ScriptEvaluation Evaluate(ModuleGraph graph);
  }
}
