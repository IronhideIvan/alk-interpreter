using ALKScript.Interpreter.Common;
using ALKScript.Interpreter.Common.Token;
using ALKScript.Interpreter.Lexer;

namespace Tests.ALKScript.Interpreter.Lexer;

public class LexicalStructureTests
{
  [Fact]
  public void Tokenize_EmptySource_ReturnsOnlyEndOfFile()
  {
    var lexer = new ALKScriptLexer();

    var tokens = lexer.Tokenize(string.Empty).ToList();

    var token = Assert.Single(tokens);
    Assert.Equal(ALKScriptTokenType.EndOfFile, token.Type);
  }

  [Fact]
  public void Tokenize_Identifier_ReturnsIdentifierToken()
  {
    var lexer = new ALKScriptLexer();

    var tokens = lexer.Tokenize("myVariable").ToList();

    Assert.Equal(ALKScriptTokenType.Identifier, tokens[0].Type);
    Assert.Equal("myVariable", tokens[0].Lexeme);
  }

  [Theory]
  [InlineData("123", "123")]
  [InlineData("3.14", "3.14")]
  [InlineData("42L", "42L")]
  [InlineData("42l", "42l")]
  public void Tokenize_Number_ReturnsNumberToken(string source, string expectedLexeme)
  {
    var lexer = new ALKScriptLexer();

    var tokens = lexer.Tokenize(source).ToList();

    Assert.Equal(ALKScriptTokenType.Number, tokens[0].Type);
    Assert.Equal(expectedLexeme, tokens[0].Lexeme);
  }

  [Fact]
  public void Tokenize_String_ReturnsStringTokenWithoutQuotes()
  {
    var lexer = new ALKScriptLexer();

    var tokens = lexer.Tokenize("\"hello world\"").ToList();

    Assert.Equal(ALKScriptTokenType.String, tokens[0].Type);
    Assert.Equal("hello world", tokens[0].Lexeme);
  }

  [Fact]
  public void Tokenize_StringWithEscapeSequences_ProcessesEscapes()
  {
    var lexer = new ALKScriptLexer();

    var tokens = lexer.Tokenize("\"line\\n\\ttabbed \\\"quoted\\\"\"").ToList();

    Assert.Equal(ALKScriptTokenType.String, tokens[0].Type);
    Assert.Equal("line\n\ttabbed \"quoted\"", tokens[0].Lexeme);
  }

  [Fact]
  public void Tokenize_LineComment_IsIgnored()
  {
    var lexer = new ALKScriptLexer();

    var tokens = lexer.Tokenize("var x = 1; // this is a comment\nvar y = 2;").ToList();

    Assert.DoesNotContain(tokens, t => t.Lexeme.Contains("comment"));
  }

  [Fact]
  public void Tokenize_BlockComment_IsIgnored()
  {
    var lexer = new ALKScriptLexer();

    var tokens = lexer.Tokenize("var x /* block comment */ = 1;").ToList();

    Assert.DoesNotContain(tokens, t => t.Lexeme.Contains("block"));
  }

  [Fact]
  public void Tokenize_Statement_ReturnsExpectedTokenSequence()
  {
    var lexer = new ALKScriptLexer();

    var tokens = lexer.Tokenize("var x = 1 + 2;").ToList();

    Assert.Equal(
      new[]
      {
        ALKScriptTokenType.Var,
        ALKScriptTokenType.Identifier,
        ALKScriptTokenType.Equal,
        ALKScriptTokenType.Number,
        ALKScriptTokenType.Plus,
        ALKScriptTokenType.Number,
        ALKScriptTokenType.Semicolon,
        ALKScriptTokenType.EndOfFile
      },
      tokens.ConvertAll(t => t.Type));
  }

  [Fact]
  public void Tokenize_MultilineSource_TracksLineNumbers()
  {
    var lexer = new ALKScriptLexer();

    var tokens = lexer.Tokenize("var x = 1;\nvar y = 2;").ToList();

    Assert.Equal(1, tokens[0].Line);
    Assert.Equal(2, tokens[5].Line);
  }
}
