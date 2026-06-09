using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>
  /// A compound assignment expression such as <c>x += 1</c> or <c>obj.count -= step</c>.
  /// Semantically equivalent to <c>target = target op value</c> but evaluates
  /// the target sub-expressions (for <see cref="GetExpr"/> and <see cref="IndexExpr"/>)
  /// exactly once, avoiding double side-effects.
  /// </summary>
  public class CompoundAssignmentExpr : Expr
  {
    public Expr Target { get; }
    public ALKScriptToken Operator { get; }
    public Expr Value { get; }

    public CompoundAssignmentExpr(Expr target, ALKScriptToken op, Expr value)
    {
      Target = target;
      Operator = op;
      Value = value;
    }
  }
}
