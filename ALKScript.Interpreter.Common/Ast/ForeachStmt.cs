using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>
  /// A <c>foreach</c> loop: <c>foreach (var item in collection) body</c>.
  /// </summary>
  public class ForeachStmt : Stmt
  {
    public ALKScriptToken Keyword { get; }
    public ALKScriptToken Variable { get; }
    public Expr Collection { get; }
    public Stmt Body { get; }

    public ForeachStmt(ALKScriptToken keyword, ALKScriptToken variable, Expr collection, Stmt body)
    {
      Keyword = keyword;
      Variable = variable;
      Collection = collection;
      Body = body;
    }
  }
}
