using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>A bare identifier reference, e.g. "x" or "add".</summary>
  public class IdentifierExpr : Expr
  {
    public ALKScriptToken Name { get; }

    public IdentifierExpr(ALKScriptToken name)
    {
      Name = name;
    }
  }
}
