namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>A "while" loop statement.</summary>
  public class WhileStmt : Stmt
  {
    public Expr Condition { get; }
    public Stmt Body { get; }

    public WhileStmt(Expr condition, Stmt body)
    {
      Condition = condition;
      Body = body;
    }
  }
}
