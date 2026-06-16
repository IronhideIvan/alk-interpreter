using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>A "typeof" expression: "typeof" unary. Evaluates to a string naming the runtime type of the operand.</summary>
  public class TypeofExpr : Expr
  {
    public ALKScriptToken Keyword { get; }
    public Expr Operand { get; }

    public TypeofExpr(ALKScriptToken keyword, Expr operand)
    {
      Keyword = keyword;
      Operand = operand;
    }
  }
}
