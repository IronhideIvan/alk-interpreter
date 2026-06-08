namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>
  /// An "export" declaration wrapping a class, function, or variable declaration.
  /// Only valid on top-level declarations.
  /// </summary>
  public class ExportDecl : Decl
  {
    public Decl Declaration { get; }

    public ExportDecl(Decl declaration)
    {
      Declaration = declaration;
    }
  }
}
