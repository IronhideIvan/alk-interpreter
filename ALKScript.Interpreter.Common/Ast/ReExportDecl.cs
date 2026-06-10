using System.Collections.Generic;
using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>
  /// A re-export declaration: "export" "{" importSpecifier ("," importSpecifier)* "}" "from" STRING ";".
  /// Re-exports the named members of another module as members of this one,
  /// optionally renaming them via the specifier's alias.
  /// </summary>
  public class ReExportDecl : Decl
  {
    public IReadOnlyList<ImportSpecifier> Specifiers { get; }
    public ALKScriptToken Source { get; }

    public ReExportDecl(IReadOnlyList<ImportSpecifier> specifiers, ALKScriptToken source)
    {
      Specifiers = specifiers;
      Source = source;
    }
  }
}
