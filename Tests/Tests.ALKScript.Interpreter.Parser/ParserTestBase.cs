using ALKScript.Interpreter.Lexer;
using ALKScript.Interpreter.Parser;
using ALKScript.Interpreter.Common.Ast;

namespace Tests.ALKScript.Interpreter.Parser;

/// <summary>
/// Shared helper for turning ALKScript source text directly into a parsed
/// <see cref="ProgramNode"/>, so individual test classes don't need to wire
/// up the lexer themselves.
/// </summary>
public abstract class ParserTestBase
{
  protected static ProgramNode Parse(string source)
  {
    var lexer = new ALKScriptLexer();
    var tokens = lexer.Tokenize(source);
    var parser = new ALKScriptParser(tokens);

    return parser.ParseProgram();
  }
}
