using ALKScript.Interpreter.Lexer;

namespace Tests.ALKScript.Interpreter.Lexer;

public class ExceptionHandlingTests
{
  [Theory]
  [InlineData("try", TokenType.Try)]
  [InlineData("catch", TokenType.Catch)]
  [InlineData("finally", TokenType.Finally)]
  [InlineData("throw", TokenType.Throw)]
  public void Tokenize_ExceptionHandlingKeyword_ReturnsExpectedToken(string source, TokenType expectedType)
  {
    var lexer = new FileLexer();

    var tokens = lexer.Tokenize(source);

    Assert.Equal(expectedType, tokens[0].Type);
    Assert.Equal(source, tokens[0].Lexeme);
  }

  [Fact]
  public void Tokenize_ThrowStatement_ReturnsExpectedTokenSequence()
  {
    var lexer = new FileLexer();

    var tokens = lexer.Tokenize("throw new InvalidAgeError(\"age cannot be negative\");");

    Assert.Equal(
      new[]
      {
        TokenType.Throw,
        TokenType.New,
        TokenType.Identifier,
        TokenType.LeftParen,
        TokenType.String,
        TokenType.RightParen,
        TokenType.Semicolon,
        TokenType.EndOfFile
      },
      tokens.ConvertAll(t => t.Type));
  }

  [Fact]
  public void Tokenize_ErrorSubclassDeclaration_ReturnsExpectedTokenSequence()
  {
    var lexer = new FileLexer();

    var tokens = lexer.Tokenize(
      "class InvalidAgeError extends Error {\n" +
      "  public new(string message) {\n" +
      "    base(message);\n" +
      "  }\n" +
      "}");

    Assert.Equal(
      new[]
      {
        TokenType.Class,
        TokenType.Identifier,
        TokenType.Extends,
        TokenType.Identifier,
        TokenType.LeftBrace,

        TokenType.Public,
        TokenType.New,
        TokenType.LeftParen,
        TokenType.StringKeyword,
        TokenType.Identifier,
        TokenType.RightParen,
        TokenType.LeftBrace,
        TokenType.Base,
        TokenType.LeftParen,
        TokenType.Identifier,
        TokenType.RightParen,
        TokenType.Semicolon,
        TokenType.RightBrace,

        TokenType.RightBrace,
        TokenType.EndOfFile
      },
      tokens.ConvertAll(t => t.Type));
  }

  [Fact]
  public void Tokenize_TryCatchFinally_ReturnsExpectedTokenSequence()
  {
    var lexer = new FileLexer();

    var tokens = lexer.Tokenize(
      "try {\n" +
      "  riskyOperation();\n" +
      "} catch (IOError e) {\n" +
      "  print(e.message);\n" +
      "} catch {\n" +
      "  print(\"failed\");\n" +
      "} finally {\n" +
      "  cleanup();\n" +
      "}");

    Assert.Equal(
      new[]
      {
        TokenType.Try,
        TokenType.LeftBrace,
        TokenType.Identifier,
        TokenType.LeftParen,
        TokenType.RightParen,
        TokenType.Semicolon,
        TokenType.RightBrace,

        TokenType.Catch,
        TokenType.LeftParen,
        TokenType.Identifier,
        TokenType.Identifier,
        TokenType.RightParen,
        TokenType.LeftBrace,
        TokenType.Identifier,
        TokenType.LeftParen,
        TokenType.Identifier,
        TokenType.Dot,
        TokenType.Identifier,
        TokenType.RightParen,
        TokenType.Semicolon,
        TokenType.RightBrace,

        TokenType.Catch,
        TokenType.LeftBrace,
        TokenType.Identifier,
        TokenType.LeftParen,
        TokenType.String,
        TokenType.RightParen,
        TokenType.Semicolon,
        TokenType.RightBrace,

        TokenType.Finally,
        TokenType.LeftBrace,
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
