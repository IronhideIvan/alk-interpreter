using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>A "continue" statement that skips to the next iteration of the nearest enclosing loop.</summary>
  public class ContinueStmt : Stmt
  {
    public ALKScriptToken Keyword { get; }

    public ContinueStmt(ALKScriptToken keyword)
    {
      Keyword = keyword;
    }
  }
}
