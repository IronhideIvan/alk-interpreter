namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>A parenthesized sub-expression: "(" expression ")".</summary>
  public class GroupingExpr : Expr
  {
    public Expr Expression { get; }

    public GroupingExpr(Expr expression)
    {
      Expression = expression;
    }
  }
}
