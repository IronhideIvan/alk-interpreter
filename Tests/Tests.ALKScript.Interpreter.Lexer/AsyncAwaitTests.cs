using ALKScript.Interpreter.Common;
using ALKScript.Interpreter.Lexer;

namespace Tests.ALKScript.Interpreter.Lexer;

public class AsyncAwaitTests
{
  [Theory]
  [InlineData("async", ALKScriptTokenType.Async)]
  [InlineData("await", ALKScriptTokenType.Await)]
  public void Tokenize_AsyncAwaitKeyword_ReturnsExpectedToken(string source, ALKScriptTokenType expectedType)
  {
    var lexer = new ALKScriptLexer();

    var tokens = lexer.Tokenize(source).ToList();

    Assert.Equal(expectedType, tokens[0].Type);
    Assert.Equal(source, tokens[0].Lexeme);
  }

  [Fact]
  public void Tokenize_AsyncFunctionDeclaration_ReturnsExpectedTokenSequence()
  {
    var lexer = new ALKScriptLexer();

    var tokens = lexer.Tokenize("async function int fetchValue() {\n  return await getValue();\n}").ToList();

    Assert.Equal(
      new[]
      {
        ALKScriptTokenType.Async,
        ALKScriptTokenType.Function,
        ALKScriptTokenType.IntKeyword,
        ALKScriptTokenType.Identifier,
        ALKScriptTokenType.LeftParen,
        ALKScriptTokenType.RightParen,
        ALKScriptTokenType.LeftBrace,
        ALKScriptTokenType.Return,
        ALKScriptTokenType.Await,
        ALKScriptTokenType.Identifier,
        ALKScriptTokenType.LeftParen,
        ALKScriptTokenType.RightParen,
        ALKScriptTokenType.Semicolon,
        ALKScriptTokenType.RightBrace,
        ALKScriptTokenType.EndOfFile
      },
      tokens.ConvertAll(t => t.Type));
  }
}
