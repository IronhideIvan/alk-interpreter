using ALKScript.Interpreter.Common.Evaluation;
using ALKScript.Interpreter.Common.Evaluation.Scheduling;
using ALKScript.Interpreter.Common.Evaluation.Values;

namespace ALKScript.Interpreter.Evaluator
{
  /// <summary>
  /// The default <see cref="IEvaluatorFactory"/> implementation. Holds any
  /// evaluator-level configuration that is fixed for the lifetime of the
  /// runtime (currently only the <see cref="IAsyncOperationBinder"/> for
  /// <c>native async</c> functions) and stamps out a fresh
  /// <see cref="ProgramEvaluator"/> per <see cref="Create"/> call, wired to
  /// the supplied scheduler and binding tables.
  /// </summary>
  public class EvaluatorFactory : IEvaluatorFactory
  {
    private readonly IAsyncOperationBinder? _operationBinder;

    public EvaluatorFactory(IAsyncOperationBinder? operationBinder = null)
    {
      _operationBinder = operationBinder;
    }

    /// <inheritdoc/>
    public IEvaluator Create(
      IScriptScheduler scheduler,
      ScriptNativeBindings bindings,
      ScriptNativeMethodBindings methodBindings) =>
      new ProgramEvaluator(bindings, methodBindings, _operationBinder, scheduler: scheduler);
  }
}
