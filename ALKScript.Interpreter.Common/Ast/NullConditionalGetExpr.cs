using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>
  /// A null-conditional member access: <c>target?.name</c>.
  /// If <see cref="Target"/> evaluates to <c>null</c> the entire expression
  /// (and any method call chained on it) short-circuits to <c>null</c> without
  /// throwing.
  /// </summary>
  public class NullConditionalGetExpr : Expr
  {
    public Expr Target { get; }
    public ALKScriptToken Name { get; }

    public NullConditionalGetExpr(Expr target, ALKScriptToken name)
    {
      Target = target;
      Name = name;
    }
  }
}
