using System.Collections.Generic;
using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>A function/method call: callee "(" arguments? ")".</summary>
  public class CallExpr : Expr
  {
    public Expr Callee { get; }
    public ALKScriptToken ClosingParen { get; }
    public IReadOnlyList<Expr> Arguments { get; }

    public CallExpr(Expr callee, ALKScriptToken closingParen, IReadOnlyList<Expr> arguments)
    {
      Callee = callee;
      ClosingParen = closingParen;
      Arguments = arguments;
    }
  }
}
