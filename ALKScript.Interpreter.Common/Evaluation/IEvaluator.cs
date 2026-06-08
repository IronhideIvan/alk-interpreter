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
    /// Evaluates <paramref name="graph"/>, starting from its entry module.
    /// </summary>
    void Evaluate(ModuleGraph graph);
  }
}
