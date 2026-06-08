using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>A namespace-import clause: "*" "as" IDENTIFIER.</summary>
  public class NamespaceImportClause : ImportClause
  {
    public ALKScriptToken Alias { get; }

    public NamespaceImportClause(ALKScriptToken alias)
    {
      Alias = alias;
    }
  }
}
