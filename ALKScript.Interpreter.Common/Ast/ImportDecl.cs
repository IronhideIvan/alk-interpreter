using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>
  /// An import declaration: "import" importClause "from" STRING ";".
  /// Import declarations must precede all other declarations in a module.
  /// </summary>
  public class ImportDecl
  {
    public ImportClause Clause { get; }
    public ALKScriptToken Source { get; }

    public ImportDecl(ImportClause clause, ALKScriptToken source)
    {
      Clause = clause;
      Source = source;
    }
  }
}
