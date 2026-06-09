using ALKScript.Interpreter.Common.Evaluation.Scheduling;
using ALKScript.Interpreter.Common.Evaluation.Values;

namespace ALKScript.Interpreter.Common.Evaluation
{
  /// <summary>
  /// Creates <see cref="IEvaluator"/> instances on demand. Each call to
  /// <see cref="Create"/> produces a fresh, independent evaluator wired to the
  /// supplied scheduler and native bindings — allowing a single
  /// <see cref="IScriptScheduler"/> to be shared across multiple concurrent
  /// script evaluations while keeping each evaluator's state (signal slot,
  /// operation log, etc.) fully isolated.
  /// </summary>
  public interface IEvaluatorFactory
  {
    /// <summary>
    /// Creates and returns a new <see cref="IEvaluator"/> that routes its
    /// async continuations through <paramref name="scheduler"/> and resolves
    /// <c>native</c> function and method declarations against the supplied
    /// binding tables.
    /// </summary>
    IEvaluator Create(
      IScriptScheduler scheduler,
      ScriptNativeBindings bindings,
      ScriptNativeMethodBindings methodBindings);
  }
}
