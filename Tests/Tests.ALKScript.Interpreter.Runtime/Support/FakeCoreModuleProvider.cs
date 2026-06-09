using System.Collections.Generic;
using System.Linq;
using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Lexer;
using ALKScript.Interpreter.Parser;
using ALKScript.Interpreter.Parser.Modules;

namespace Tests.ALKScript.Interpreter.Runtime.Support;

/// <summary>
/// An in-memory <see cref="ICoreModuleProvider"/> that lexes and parses its
/// modules from plain ALKScript source text, so tests can describe a fake
/// standard library without a real provider.
/// </summary>
internal class FakeCoreModuleProvider : ICoreModuleProvider
{
  private readonly IReadOnlyDictionary<string, ProgramNode> _modules;

  public FakeCoreModuleProvider(IReadOnlyDictionary<string, string> sourcesBySpecifier)
  {
    var lexer = new ALKScriptLexer();
    var parser = new ALKScriptParser();

    _modules = sourcesBySpecifier.ToDictionary(
      e => e.Key,
      e => parser.ParseTokens(lexer.Tokenize(e.Value)));
  }

  public IReadOnlyCollection<string> AvailableModules => _modules.Keys.ToList();

  public ProgramNode GetModule(string specifier) => _modules[specifier];
}
