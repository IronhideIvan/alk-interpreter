using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>An "await" expression: "await" unary.</summary>
  public class AwaitExpr : Expr
  {
    public ALKScriptToken Keyword { get; }
    public Expr Operand { get; }

    public AwaitExpr(ALKScriptToken keyword, Expr operand)
    {
      Keyword = keyword;
      Operand = operand;
    }
  }
}
