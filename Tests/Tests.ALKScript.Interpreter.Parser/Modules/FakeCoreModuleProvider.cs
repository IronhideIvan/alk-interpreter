using System.Collections.Generic;
using System.Linq;
using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Common.Modules;
using ALKScript.Interpreter.Lexer;
using ALKScript.Interpreter.Parser.Modules;
using ALKScriptParser = ALKScript.Interpreter.Parser.ALKScriptParser;

namespace Tests.ALKScript.Interpreter.Parser.Modules;

/// <summary>
/// An in-memory <see cref="ICoreModuleProvider"/> that lexes and parses its
/// definitions from plain ALKScript source text up front, so tests can
/// describe a fake standard library declaratively without a real provider.
/// </summary>
public class FakeCoreModuleProvider : ICoreModuleProvider
{
  private readonly IReadOnlyDictionary<string, ProgramNode> _modules;

  public FakeCoreModuleProvider(IReadOnlyDictionary<string, string> sourcesBySpecifier)
  {
    var lexer = new ALKScriptLexer();
    var parser = new ALKScriptParser();

    var modules = new Dictionary<string, ProgramNode>();

    foreach (KeyValuePair<string, string> entry in sourcesBySpecifier)
    {
      modules[entry.Key] = parser.ParseTokens(lexer.Tokenize(entry.Value));
    }

    _modules = modules;
  }

  public IReadOnlyCollection<string> AvailableModules => _modules.Keys.ToList();

  public ProgramNode GetModule(string specifier)
  {
    return _modules[specifier];
  }
}
