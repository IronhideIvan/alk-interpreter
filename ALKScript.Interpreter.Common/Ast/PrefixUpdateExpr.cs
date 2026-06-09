using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>A prefix increment or decrement: <c>++operand</c> or <c>--operand</c>.
  /// Evaluates to the value <em>after</em> the update.</summary>
  public class PrefixUpdateExpr : Expr
  {
    public ALKScriptToken Operator { get; }
    public Expr Operand { get; }

    public PrefixUpdateExpr(ALKScriptToken op, Expr operand)
    {
      Operator = op;
      Operand  = operand;
    }
  }
}
