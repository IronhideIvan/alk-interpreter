using ALKScript.Interpreter.Common.Evaluation;

namespace ALKScript.Interpreter.Evaluator
{
  /// <summary>
  /// Builds the <see cref="IStatementExecutor"/>, <see cref="IExpressionEvaluator"/>
  /// and <see cref="ICallInvoker"/> collaborators that a <see cref="ProgramEvaluator"/>
  /// composes, each wired up against the shared <see cref="IEvaluationContext"/>.
  ///
  /// Routing their construction through a factory lets <see cref="ProgramEvaluator"/>
  /// stay agnostic of the concrete collaborator types — useful for substituting
  /// alternative implementations, e.g. in tests.
  /// </summary>
  internal interface IEvaluationComponentFactory
  {
    IStatementExecutor CreateStatementExecutor(IEvaluationContext context, IFunctionValueFactory functionValueFactory);

    IExpressionEvaluator CreateExpressionEvaluator(IEvaluationContext context, IFunctionValueFactory functionValueFactory);

    ICallInvoker CreateCallInvoker(IEvaluationContext context);
  }
}
