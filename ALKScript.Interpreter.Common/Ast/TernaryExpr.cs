using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>
  /// A ternary conditional expression: <c>condition ? thenExpr : elseExpr</c>.
  /// Only one branch is evaluated — the branch corresponding to the truthiness
  /// of <see cref="Condition"/>.
  /// </summary>
  public class TernaryExpr : Expr
  {
    public Expr Condition { get; }
    public ALKScriptToken QuestionToken { get; }
    public Expr ThenExpr { get; }
    public Expr ElseExpr { get; }

    public TernaryExpr(Expr condition, ALKScriptToken questionToken, Expr thenExpr, Expr elseExpr)
    {
      Condition = condition;
      QuestionToken = questionToken;
      ThenExpr = thenExpr;
      ElseExpr = elseExpr;
    }
  }
}
