using System.Collections.Generic;
using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>
  /// A single <c>case</c> (or <c>default</c> when <see cref="Test"/> is
  /// <c>null</c>) within a <see cref="SwitchStmt"/>.
  /// </summary>
  public class SwitchCase
  {
    /// <summary>The <c>case</c> value expression, or <c>null</c> for <c>default</c>.</summary>
    public Expr? Test { get; }

    /// <summary>The statements that run when this case is matched (or fallen through to).</summary>
    public IReadOnlyList<Stmt> Body { get; }

    public SwitchCase(Expr? test, IReadOnlyList<Stmt> body)
    {
      Test = test;
      Body = body;
    }
  }

  /// <summary>
  /// A C-style <c>switch</c> statement: <c>switch (discriminant) { case x: ... default: ... }</c>.
  /// Cases fall through to the next case unless terminated by <c>break</c>.
  /// </summary>
  public class SwitchStmt : Stmt
  {
    public ALKScriptToken Keyword { get; }
    public Expr Discriminant { get; }
    public IReadOnlyList<SwitchCase> Cases { get; }

    public SwitchStmt(ALKScriptToken keyword, Expr discriminant, IReadOnlyList<SwitchCase> cases)
    {
      Keyword = keyword;
      Discriminant = discriminant;
      Cases = cases;
    }
  }
}
