using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>A postfix increment or decrement: <c>operand++</c> or <c>operand--</c>.
  /// Evaluates to the value <em>before</em> the update.</summary>
  public class PostfixUpdateExpr : Expr
  {
    public Expr Operand { get; }
    public ALKScriptToken Operator { get; }

    public PostfixUpdateExpr(Expr operand, ALKScriptToken op)
    {
      Operand  = operand;
      Operator = op;
    }
  }
}
