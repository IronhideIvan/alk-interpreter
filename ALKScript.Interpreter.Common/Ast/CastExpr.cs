using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>
  /// A C-style numeric conversion cast: <c>"(" ("int" | "long" | "float") ")" unary</c>.
  /// Unlike <see cref="TypeCastExpr"/> ("as"), this performs an actual numeric
  /// conversion (truncating "float" to "int"/"long", or widening "int"/"long"
  /// to "float") rather than a type check.
  /// </summary>
  public class CastExpr : Expr
  {
    public ALKScriptToken Keyword { get; }
    public string TargetType { get; }
    public Expr Operand { get; }

    public CastExpr(ALKScriptToken keyword, string targetType, Expr operand)
    {
      Keyword = keyword;
      TargetType = targetType;
      Operand = operand;
    }
  }
}
