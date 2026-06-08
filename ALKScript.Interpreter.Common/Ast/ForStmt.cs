namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>
  /// A C-style "for" loop statement. Any of the three clauses may be absent,
  /// matching the grammar's "for" "(" (variableDecl | exprStatement | ";")
  /// expression? ";" expression? ")" statement.
  /// </summary>
  public class ForStmt : Stmt
  {
    public Stmt? Initializer { get; }
    public Expr? Condition { get; }
    public Expr? Increment { get; }
    public Stmt Body { get; }

    public ForStmt(Stmt? initializer, Expr? condition, Expr? increment, Stmt body)
    {
      Initializer = initializer;
      Condition = condition;
      Increment = increment;
      Body = body;
    }
  }
}
