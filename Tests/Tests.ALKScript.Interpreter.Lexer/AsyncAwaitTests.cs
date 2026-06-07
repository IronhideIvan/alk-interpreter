using ALKScript.Interpreter.Lexer;

namespace Tests.ALKScript.Interpreter.Lexer;

public class AsyncAwaitTests
{
  [Theory]
  [InlineData("async", TokenType.Async)]
  [InlineData("await", TokenType.Await)]
  public void Tokenize_AsyncAwaitKeyword_ReturnsExpectedToken(string source, TokenType expectedType)
  {
    var lexer = new FileLexer();

    var tokens = lexer.Tokenize(source);

    Assert.Equal(expectedType, tokens[0].Type);
    Assert.Equal(source, tokens[0].Lexeme);
  }

  [Fact]
  public void Tokenize_AsyncFunctionDeclaration_ReturnsExpectedTokenSequence()
  {
    var lexer = new FileLexer();

    var tokens = lexer.Tokenize("async function int fetchValue() {\n  return await getValue();\n}");

    Assert.Equal(
      new[]
      {
        TokenType.Async,
        TokenType.Function,
        TokenType.IntKeyword,
        TokenType.Identifier,
        TokenType.LeftParen,
        TokenType.RightParen,
        TokenType.LeftBrace,
        TokenType.Return,
        TokenType.Await,
        TokenType.Identifier,
        TokenType.LeftParen,
        TokenType.RightParen,
        TokenType.Semicolon,
        TokenType.RightBrace,
        TokenType.EndOfFile
      },
      tokens.ConvertAll(t => t.Type));
  }
}
