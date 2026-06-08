using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>A member-access expression: object "." IDENTIFIER.</summary>
  public class GetExpr : Expr
  {
    public Expr Target { get; }
    public ALKScriptToken Name { get; }

    public GetExpr(Expr target, ALKScriptToken name)
    {
      Target = target;
      Name = name;
    }
  }
}
