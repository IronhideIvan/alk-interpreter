using ALKScript.Interpreter.Lexer;

namespace Tests.ALKScript.Interpreter.Lexer;

public class FunctionsTests
{
  [Theory]
  [InlineData("if", TokenType.If)]
  [InlineData("else", TokenType.Else)]
  [InlineData("while", TokenType.While)]
  [InlineData("for", TokenType.For)]
  [InlineData("function", TokenType.Function)]
  [InlineData("return", TokenType.Return)]
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
  public void Tokenize_FunctionDeclarationWithReturnType_ReturnsExpectedTokenSequence()
  {
    var lexer = new FileLexer();

    var tokens = lexer.Tokenize("function int add(int a, int b) {\n  return a + b;\n}");

    Assert.Equal(
      new[]
      {
        TokenType.Function,
        TokenType.IntKeyword,
        TokenType.Identifier,
        TokenType.LeftParen,
        TokenType.IntKeyword,
        TokenType.Identifier,
        TokenType.Comma,
        TokenType.IntKeyword,
        TokenType.Identifier,
        TokenType.RightParen,
        TokenType.LeftBrace,
        TokenType.Return,
        TokenType.Identifier,
        TokenType.Plus,
        TokenType.Identifier,
        TokenType.Semicolon,
        TokenType.RightBrace,
        TokenType.EndOfFile
      },
      tokens.ConvertAll(t => t.Type));
  }

  [Fact]
  public void Tokenize_VoidFunctionDeclaration_ReturnsExpectedTokenSequence()
  {
    var lexer = new FileLexer();

    var tokens = lexer.Tokenize("function void log(string message) {\n  print(message);\n}");

    Assert.Equal(
      new[]
      {
        TokenType.Function,
        TokenType.VoidKeyword,
        TokenType.Identifier,
        TokenType.LeftParen,
        TokenType.StringKeyword,
        TokenType.Identifier,
        TokenType.RightParen,
        TokenType.LeftBrace,
        TokenType.Identifier,
        TokenType.LeftParen,
        TokenType.Identifier,
        TokenType.RightParen,
        TokenType.Semicolon,
        TokenType.RightBrace,
        TokenType.EndOfFile
      },
      tokens.ConvertAll(t => t.Type));
  }

  [Fact]
  public void Tokenize_IfElseStatement_ReturnsExpectedTokenSequence()
  {
    var lexer = new FileLexer();

    var tokens = lexer.Tokenize("if (a < b) { return a; } else { return b; }");

    Assert.Equal(TokenType.If, tokens[0].Type);
    Assert.Equal(TokenType.Else, tokens[11].Type);
  }
}
