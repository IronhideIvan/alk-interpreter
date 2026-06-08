using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>
  /// A binary operator expression covering logical (||, &amp;&amp;), equality
  /// (==, !=), comparison (&lt;, &lt;=, &gt;, &gt;=), and arithmetic
  /// (+, -, *, /, %) operators.
  /// </summary>
  public class BinaryExpr : Expr
  {
    public Expr Left { get; }
    public ALKScriptToken Operator { get; }
    public Expr Right { get; }

    public BinaryExpr(Expr left, ALKScriptToken op, Expr right)
    {
      Left = left;
      Operator = op;
      Right = right;
    }
  }
}
