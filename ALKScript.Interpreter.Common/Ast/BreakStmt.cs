using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>A "break" statement that exits the nearest enclosing loop.</summary>
  public class BreakStmt : Stmt
  {
    public ALKScriptToken Keyword { get; }

    public BreakStmt(ALKScriptToken keyword)
    {
      Keyword = keyword;
    }
  }
}
