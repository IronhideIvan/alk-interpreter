using System.Collections.Generic;

namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>An array literal: "[" arguments? "]".</summary>
  public class ArrayLiteralExpr : Expr
  {
    public IReadOnlyList<Expr> Elements { get; }

    public ArrayLiteralExpr(IReadOnlyList<Expr> elements)
    {
      Elements = elements;
    }
  }
}
