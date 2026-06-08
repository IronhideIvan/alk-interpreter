using ALKScript.Interpreter.Common.Evaluation;

namespace ALKScript.Interpreter.Evaluator
{
  /// <summary>
  /// Default <see cref="IEvaluationComponentFactory"/>: builds the production
  /// <see cref="StatementExecutor"/>, <see cref="ExpressionEvaluator"/> and
  /// <see cref="CallInvoker"/> collaborators.
  /// </summary>
  internal class EvaluationComponentFactory : IEvaluationComponentFactory
  {
    public IStatementExecutor CreateStatementExecutor(IEvaluationContext context, IFunctionValueFactory functionValueFactory)
      => new StatementExecutor(context, functionValueFactory);

    public IExpressionEvaluator CreateExpressionEvaluator(IEvaluationContext context, IFunctionValueFactory functionValueFactory)
      => new ExpressionEvaluator(context, functionValueFactory);

    public ICallInvoker CreateCallInvoker(IEvaluationContext context)
      => new CallInvoker(context);
  }
}
