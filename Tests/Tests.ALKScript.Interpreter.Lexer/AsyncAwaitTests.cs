using ALKScript.Interpreter.Common;
using ALKScript.Interpreter.Common.Token;
using ALKScript.Interpreter.Lexer;

namespace Tests.ALKScript.Interpreter.Lexer;

public class AsyncAwaitTests
{
  [Theory]
  [InlineData("await", ALKScriptTokenType.Await)]
  [InlineData("thunk", ALKScriptTokenType.Thunk)]
  [InlineData("typeof", ALKScriptTokenType.Typeof)]
  public void Tokenize_AsyncAwaitKeyword_ReturnsExpectedToken(string source, ALKScriptTokenType expectedType)
  {
    var lexer = new ALKScriptLexer();

    var tokens = lexer.Tokenize(source).ToList();

    Assert.Equal(expectedType, tokens[0].Type);
    Assert.Equal(source, tokens[0].Lexeme);
  }

  [Fact]
  public void Tokenize_ThunkFunctionDeclaration_ReturnsExpectedTokenSequence()
  {
    var lexer = new ALKScriptLexer();

    var tokens = lexer.Tokenize("function thunk<int> fetchValue() {\n  return await getValue();\n}").ToList();

    Assert.Equal(
      new[]
      {
        ALKScriptTokenType.Function,
        ALKScriptTokenType.Thunk,
        ALKScriptTokenType.Less,
        ALKScriptTokenType.IntKeyword,
        ALKScriptTokenType.Greater,
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
