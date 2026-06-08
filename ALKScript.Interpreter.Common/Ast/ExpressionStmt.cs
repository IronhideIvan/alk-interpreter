namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>An expression used as a statement: expression ";".</summary>
  public class ExpressionStmt : Stmt
  {
    public Expr Expression { get; }

    public ExpressionStmt(Expr expression)
    {
      Expression = expression;
    }
  }
}
