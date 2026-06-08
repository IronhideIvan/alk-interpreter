using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>The "base" keyword, referring to the superclass instance/constructor.</summary>
  public class BaseExpr : Expr
  {
    public ALKScriptToken Keyword { get; }

    public BaseExpr(ALKScriptToken keyword)
    {
      Keyword = keyword;
    }
  }
}
