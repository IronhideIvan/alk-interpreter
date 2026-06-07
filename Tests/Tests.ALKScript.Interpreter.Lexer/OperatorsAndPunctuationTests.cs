using ALKScript.Interpreter.Lexer;

namespace Tests.ALKScript.Interpreter.Lexer;

public class OperatorsAndPunctuationTests
{
  [Theory]
  [InlineData("+", TokenType.Plus)]
  [InlineData("-", TokenType.Minus)]
  [InlineData("*", TokenType.Star)]
  [InlineData("/", TokenType.Slash)]
  [InlineData("%", TokenType.Percent)]
  [InlineData("=", TokenType.Equal)]
  [InlineData("==", TokenType.EqualEqual)]
  [InlineData("!", TokenType.Bang)]
  [InlineData("!=", TokenType.BangEqual)]
  [InlineData("<", TokenType.Less)]
  [InlineData("<=", TokenType.LessEqual)]
  [InlineData(">", TokenType.Greater)]
  [InlineData(">=", TokenType.GreaterEqual)]
  [InlineData("&&", TokenType.AmpAmp)]
  [InlineData("||", TokenType.PipePipe)]
  [InlineData("(", TokenType.LeftParen)]
  [InlineData(")", TokenType.RightParen)]
  [InlineData("{", TokenType.LeftBrace)]
  [InlineData("}", TokenType.RightBrace)]
  [InlineData("[", TokenType.LeftBracket)]
  [InlineData("]", TokenType.RightBracket)]
  [InlineData(",", TokenType.Comma)]
  [InlineData(";", TokenType.Semicolon)]
  [InlineData(":", TokenType.Colon)]
  [InlineData(".", TokenType.Dot)]
  [InlineData("?", TokenType.Question)]
  public void Tokenize_OperatorOrPunctuation_ReturnsExpectedToken(string source, TokenType expectedType)
  {
    var lexer = new FileLexer();

    var tokens = lexer.Tokenize(source);

    Assert.Equal(expectedType, tokens[0].Type);
    Assert.Equal(source, tokens[0].Lexeme);
  }

  [Fact]
  public void Tokenize_NullableTypeAnnotation_ReturnsQuestionToken()
  {
    var lexer = new FileLexer();

    var tokens = lexer.Tokenize("string? name = null;");

    Assert.Equal(
      new[]
      {
        TokenType.StringKeyword,
        TokenType.Question,
        TokenType.Identifier,
        TokenType.Equal,
        TokenType.Null,
        TokenType.Semicolon,
        TokenType.EndOfFile
      },
      tokens.ConvertAll(t => t.Type));
  }
}
