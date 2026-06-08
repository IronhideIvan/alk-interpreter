using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>A single named import specifier: IDENTIFIER ("as" IDENTIFIER)?.</summary>
  public class ImportSpecifier
  {
    public ALKScriptToken Name { get; }
    public ALKScriptToken? Alias { get; }

    public ImportSpecifier(ALKScriptToken name, ALKScriptToken? alias)
    {
      Name = name;
      Alias = alias;
    }
  }
}
