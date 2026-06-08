namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>An assignment expression: "IDENTIFIER" "=" assignment.</summary>
  public class AssignmentExpr : Expr
  {
    public Expr Target { get; }
    public Expr Value { get; }

    public AssignmentExpr(Expr target, Expr value)
    {
      Target = target;
      Value = value;
    }
  }
}
