using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>
  /// A <c>do…while</c> loop: <c>do body while (condition);</c>.
  /// The body is always executed at least once before the condition is tested.
  /// </summary>
  public class DoWhileStmt : Stmt
  {
    public ALKScriptToken Keyword { get; }
    public Stmt Body { get; }
    public Expr Condition { get; }

    public DoWhileStmt(ALKScriptToken keyword, Stmt body, Expr condition)
    {
      Keyword = keyword;
      Body = body;
      Condition = condition;
    }
  }
}
