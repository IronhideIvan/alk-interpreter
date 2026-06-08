using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Common.Evaluation;
using ALKScript.Interpreter.Common.Evaluation.Values;

namespace ALKScript.Interpreter.Evaluator
{
  /// <summary>
  /// Evaluates expressions: dispatches on <see cref="Expr"/> shape, producing
  /// <see cref="ALKScriptValue"/>s.
  /// </summary>
  internal interface IExpressionEvaluator
  {
    ALKScriptValue Eval(Expr expression, ScriptEnvironment environment);
  }
}
