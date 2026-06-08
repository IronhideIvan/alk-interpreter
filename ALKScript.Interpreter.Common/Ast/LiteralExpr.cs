using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>A literal value: number, string, "true", "false", or "null".</summary>
  public class LiteralExpr : Expr
  {
    public ALKScriptToken Token { get; }
    public object? Value { get; }

    public LiteralExpr(ALKScriptToken token, object? value)
    {
      Token = token;
      Value = value;
    }
  }
}
