using System.Collections.Generic;

namespace ALKScript.Interpreter.Parser.Ast
{
  /// <summary>
  /// The root AST node for a parsed module/file:
  ///   program = importDecl* declaration* EOF ;
  /// </summary>
  public class ProgramNode
  {
    public IReadOnlyList<ImportDecl> Imports { get; }

    /// <summary>
    /// The module's top-level declarations. Per the grammar, "declaration"
    /// includes plain statements as well as class/function/variable/export
    /// declarations, so this list may contain any <see cref="Stmt"/>
    /// (declarations are represented as <see cref="Decl"/>, a subtype of
    /// <see cref="Stmt"/>).
    /// </summary>
    public IReadOnlyList<Stmt> Declarations { get; }

    public ProgramNode(IReadOnlyList<ImportDecl> imports, IReadOnlyList<Stmt> declarations)
    {
      Imports = imports;
      Declarations = declarations;
    }
  }
}
