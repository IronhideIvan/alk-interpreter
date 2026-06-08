using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>A unary prefix expression: "!" | "-" applied to its operand.</summary>
  public class UnaryExpr : Expr
  {
    public ALKScriptToken Operator { get; }
    public Expr Operand { get; }

    public UnaryExpr(ALKScriptToken op, Expr operand)
    {
      Operator = op;
      Operand = operand;
    }
  }
}
