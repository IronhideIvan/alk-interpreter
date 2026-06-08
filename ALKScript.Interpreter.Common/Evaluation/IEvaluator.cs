using System.Threading.Tasks;
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
    /// Returns a <see cref="Task"/> because evaluation may suspend mid-script
    /// (e.g. on an <c>await</c> of a host operation) and resume later — the
    /// task completes once the script has finished running to completion (or
    /// unwound via an uncaught "throw"/external "cancel").
    /// </summary>
    Task Evaluate(ModuleGraph graph);
  }
}
