using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>
  /// A type-casting expression: <c>expr "as" type</c>. Evaluates to
  /// <see cref="Operand"/>'s value when it is an instance of
  /// <see cref="Type"/>, or <c>null</c> otherwise — a safe cast, mirroring
  /// C#'s <c>as</c> operator rather than throwing on mismatch.
  /// </summary>
  public class TypeCastExpr : Expr
  {
    public Expr Operand { get; }
    public ALKScriptToken Keyword { get; }
    public TypeNode Type { get; }

    public TypeCastExpr(Expr operand, ALKScriptToken keyword, TypeNode type)
    {
      Operand = operand;
      Keyword = keyword;
      Type = type;
    }
  }
}
