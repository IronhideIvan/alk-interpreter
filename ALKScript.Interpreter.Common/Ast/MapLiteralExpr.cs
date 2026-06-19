using System.Collections.Generic;
using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>
  /// A map literal: "new" "map" "&lt;" keyType "," valueType "&gt;" "{" (expr ":" expr)* "}".
  /// </summary>
  public class MapLiteralExpr : Expr
  {
    public ALKScriptToken Keyword { get; }
    public TypeNode KeyType { get; }
    public TypeNode ValueType { get; }
    public IReadOnlyList<(Expr Key, Expr Value)> Entries { get; }

    public MapLiteralExpr(ALKScriptToken keyword, TypeNode keyType, TypeNode valueType, IReadOnlyList<(Expr, Expr)> entries)
    {
      Keyword = keyword;
      KeyType = keyType;
      ValueType = valueType;
      Entries = entries;
    }
  }
}
