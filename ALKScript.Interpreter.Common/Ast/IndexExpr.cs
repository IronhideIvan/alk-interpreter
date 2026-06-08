using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>An indexing expression: object "[" expression "]".</summary>
  public class IndexExpr : Expr
  {
    public Expr Target { get; }
    public Expr Index { get; }
    public ALKScriptToken ClosingBracket { get; }

    public IndexExpr(Expr target, Expr index, ALKScriptToken closingBracket)
    {
      Target = target;
      Index = index;
      ClosingBracket = closingBracket;
    }
  }
}
