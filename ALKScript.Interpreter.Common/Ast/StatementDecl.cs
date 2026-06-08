namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>
  /// Wraps a plain statement so it can appear in a "declaration*" list. The
  /// grammar's "declaration" production permits ordinary statements (e.g.
  /// expression statements, control flow) alongside class/function/variable
  /// declarations; this node lets the parser represent that list uniformly
  /// as <see cref="Decl"/>/<see cref="Stmt"/> values without losing the
  /// distinction between a "real" declaration and a wrapped statement.
  /// </summary>
  public class StatementDecl : Decl
  {
    public Stmt Statement { get; }

    public StatementDecl(Stmt statement)
    {
      Statement = statement;
    }
  }
}
