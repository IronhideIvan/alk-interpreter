using ALK.Interpreter.Lexer;

namespace Tests.ALK.Interpreter.Lexer;

public class FileLexerTests
{
  [Fact]
  public void Tokenize_EmptySource_ReturnsOnlyEndOfFile()
  {
    var lexer = new FileLexer();

    var tokens = lexer.Tokenize(string.Empty);

    var token = Assert.Single(tokens);
    Assert.Equal(TokenType.EndOfFile, token.Type);
  }

  [Theory]
  [InlineData("if", TokenType.If)]
  [InlineData("else", TokenType.Else)]
  [InlineData("while", TokenType.While)]
  [InlineData("for", TokenType.For)]
  [InlineData("function", TokenType.Function)]
  [InlineData("return", TokenType.Return)]
  [InlineData("let", TokenType.Let)]
  [InlineData("true", TokenType.True)]
  [InlineData("false", TokenType.False)]
  [InlineData("null", TokenType.Null)]
  public void Tokenize_Keyword_ReturnsKeywordToken(string source, TokenType expectedType)
  {
    var lexer = new FileLexer();

    var tokens = lexer.Tokenize(source);

    Assert.Equal(expectedType, tokens[0].Type);
    Assert.Equal(source, tokens[0].Lexeme);
  }

  [Fact]
  public void Tokenize_Identifier_ReturnsIdentifierToken()
  {
    var lexer = new FileLexer();

    var tokens = lexer.Tokenize("myVariable");

    Assert.Equal(TokenType.Identifier, tokens[0].Type);
    Assert.Equal("myVariable", tokens[0].Lexeme);
  }

  [Theory]
  [InlineData("123", "123")]
  [InlineData("3.14", "3.14")]
  public void Tokenize_Number_ReturnsNumberToken(string source, string expectedLexeme)
  {
    var lexer = new FileLexer();

    var tokens = lexer.Tokenize(source);

    Assert.Equal(TokenType.Number, tokens[0].Type);
    Assert.Equal(expectedLexeme, tokens[0].Lexeme);
  }

  [Fact]
  public void Tokenize_String_ReturnsStringTokenWithoutQuotes()
  {
    var lexer = new FileLexer();

    var tokens = lexer.Tokenize("\"hello world\"");

    Assert.Equal(TokenType.String, tokens[0].Type);
    Assert.Equal("hello world", tokens[0].Lexeme);
  }

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
  public void Tokenize_OperatorOrPunctuation_ReturnsExpectedToken(string source, TokenType expectedType)
  {
    var lexer = new FileLexer();

    var tokens = lexer.Tokenize(source);

    Assert.Equal(expectedType, tokens[0].Type);
    Assert.Equal(source, tokens[0].Lexeme);
  }

  [Fact]
  public void Tokenize_LineComment_IsIgnored()
  {
    var lexer = new FileLexer();

    var tokens = lexer.Tokenize("let x = 1; // this is a comment\nlet y = 2;");

    Assert.DoesNotContain(tokens, t => t.Lexeme.Contains("comment"));
  }

  [Fact]
  public void Tokenize_BlockComment_IsIgnored()
  {
    var lexer = new FileLexer();

    var tokens = lexer.Tokenize("let x /* block comment */ = 1;");

    Assert.DoesNotContain(tokens, t => t.Lexeme.Contains("block"));
  }

  [Fact]
  public void Tokenize_Statement_ReturnsExpectedTokenSequence()
  {
    var lexer = new FileLexer();

    var tokens = lexer.Tokenize("let x = 1 + 2;");

    Assert.Equal(
      new[]
      {
        TokenType.Let,
        TokenType.Identifier,
        TokenType.Equal,
        TokenType.Number,
        TokenType.Plus,
        TokenType.Number,
        TokenType.Semicolon,
        TokenType.EndOfFile
      },
      tokens.ConvertAll(t => t.Type));
  }

  [Fact]
  public void Tokenize_MultilineSource_TracksLineNumbers()
  {
    var lexer = new FileLexer();

    var tokens = lexer.Tokenize("let x = 1;\nlet y = 2;");

    Assert.Equal(1, tokens[0].Line);
    Assert.Equal(2, tokens[5].Line);
  }
}
