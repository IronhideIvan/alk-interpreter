using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>A "throw" statement: "throw" expression ";".</summary>
  public class ThrowStmt : Stmt
  {
    public ALKScriptToken Keyword { get; }
    public Expr Value { get; }

    public ThrowStmt(ALKScriptToken keyword, Expr value)
    {
      Keyword = keyword;
      Value = value;
    }
  }
}
