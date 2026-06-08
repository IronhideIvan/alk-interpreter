using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>A "return" statement with an optional expression.</summary>
  public class ReturnStmt : Stmt
  {
    public ALKScriptToken Keyword { get; }
    public Expr? Value { get; }

    public ReturnStmt(ALKScriptToken keyword, Expr? value)
    {
      Keyword = keyword;
      Value = value;
    }
  }
}
