namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>An "if" statement, with an optional "else" branch.</summary>
  public class IfStmt : Stmt
  {
    public Expr Condition { get; }
    public Stmt ThenBranch { get; }
    public Stmt? ElseBranch { get; }

    public IfStmt(Expr condition, Stmt thenBranch, Stmt? elseBranch)
    {
      Condition = condition;
      ThenBranch = thenBranch;
      ElseBranch = elseBranch;
    }
  }
}
