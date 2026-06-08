using System.Threading.Tasks;
using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Common.Evaluation;
using ALKScript.Interpreter.Common.Evaluation.Values;

namespace ALKScript.Interpreter.Evaluator
{
  /// <summary>
  /// Evaluates expressions: dispatches on <see cref="Expr"/> shape, producing
  /// <see cref="ALKScriptValue"/>s. <see cref="Task"/>-returning so that an
  /// <c>await</c> anywhere in the expression tree can suspend evaluation and
  /// resume later without losing in-flight evaluation state.
  /// </summary>
  internal interface IExpressionEvaluator
  {
    Task<ALKScriptValue> Eval(Expr expression, ScriptEnvironment environment);
  }
}
