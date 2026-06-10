using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>
  /// A type-testing expression: <c>expr "is" type</c>. Evaluates to a
  /// <c>bool</c> indicating whether <see cref="Operand"/>'s runtime value
  /// is an instance of <see cref="Type"/> (or <c>null</c> when
  /// <see cref="Type"/> is nullable and the operand is <c>null</c>).
  /// </summary>
  public class TypeTestExpr : Expr
  {
    public Expr Operand { get; }
    public ALKScriptToken Keyword { get; }
    public TypeNode Type { get; }

    public TypeTestExpr(Expr operand, ALKScriptToken keyword, TypeNode type)
    {
      Operand = operand;
      Keyword = keyword;
      Type = type;
    }
  }
}
