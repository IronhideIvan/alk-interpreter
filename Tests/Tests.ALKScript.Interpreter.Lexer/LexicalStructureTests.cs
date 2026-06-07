using ALKScript.Interpreter.Lexer;

namespace Tests.ALKScript.Interpreter.Lexer;

public class LexicalStructureTests
{
  [Fact]
  public void Tokenize_EmptySource_ReturnsOnlyEndOfFile()
  {
    var lexer = new FileLexer();

    var tokens = lexer.Tokenize(string.Empty);

    var token = Assert.Single(tokens);
    Assert.Equal(TokenType.EndOfFile, token.Type);
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
  [InlineData("42L", "42L")]
  [InlineData("42l", "42l")]
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

  [Fact]
  public void Tokenize_StringWithEscapeSequences_ProcessesEscapes()
  {
    var lexer = new FileLexer();

    var tokens = lexer.Tokenize("\"line\\n\\ttabbed \\\"quoted\\\"\"");

    Assert.Equal(TokenType.String, tokens[0].Type);
    Assert.Equal("line\n\ttabbed \"quoted\"", tokens[0].Lexeme);
  }

  [Fact]
  public void Tokenize_LineComment_IsIgnored()
  {
    var lexer = new FileLexer();

    var tokens = lexer.Tokenize("var x = 1; // this is a comment\nvar y = 2;");

    Assert.DoesNotContain(tokens, t => t.Lexeme.Contains("comment"));
  }

  [Fact]
  public void Tokenize_BlockComment_IsIgnored()
  {
    var lexer = new FileLexer();

    var tokens = lexer.Tokenize("var x /* block comment */ = 1;");

    Assert.DoesNotContain(tokens, t => t.Lexeme.Contains("block"));
  }

  [Fact]
  public void Tokenize_Statement_ReturnsExpectedTokenSequence()
  {
    var lexer = new FileLexer();

    var tokens = lexer.Tokenize("var x = 1 + 2;");

    Assert.Equal(
      new[]
      {
        TokenType.Var,
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

    var tokens = lexer.Tokenize("var x = 1;\nvar y = 2;");

    Assert.Equal(1, tokens[0].Line);
    Assert.Equal(2, tokens[5].Line);
  }
}
