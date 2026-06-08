using System.Collections.Generic;

namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>A named-imports clause: "{" importSpecifier ("," importSpecifier)* "}".</summary>
  public class NamedImportsClause : ImportClause
  {
    public IReadOnlyList<ImportSpecifier> Specifiers { get; }

    public NamedImportsClause(IReadOnlyList<ImportSpecifier> specifiers)
    {
      Specifiers = specifiers;
    }
  }
}
