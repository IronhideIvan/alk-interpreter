using System.Collections.Generic;
using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>An object-instantiation expression: "new" IDENTIFIER ("&lt;" type, ... "&gt;")? "(" arguments? ")".</summary>
  public class NewExpr : Expr
  {
    public ALKScriptToken Keyword { get; }
    public ALKScriptToken TypeName { get; }
    public IReadOnlyList<TypeNode> TypeArguments { get; }
    public IReadOnlyList<Expr> Arguments { get; }

    public NewExpr(ALKScriptToken keyword, ALKScriptToken typeName, IReadOnlyList<TypeNode> typeArguments, IReadOnlyList<Expr> arguments)
    {
      Keyword = keyword;
      TypeName = typeName;
      TypeArguments = typeArguments;
      Arguments = arguments;
    }
  }
}
