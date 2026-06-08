using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>The "this" keyword, referring to the current instance.</summary>
  public class ThisExpr : Expr
  {
    public ALKScriptToken Keyword { get; }

    public ThisExpr(ALKScriptToken keyword)
    {
      Keyword = keyword;
    }
  }
}
