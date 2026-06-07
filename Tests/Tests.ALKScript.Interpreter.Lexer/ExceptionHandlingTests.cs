using ALKScript.Interpreter.Common;
using ALKScript.Interpreter.Common.Token;
using ALKScript.Interpreter.Lexer;

namespace Tests.ALKScript.Interpreter.Lexer;

public class ExceptionHandlingTests
{
  [Theory]
  [InlineData("try", ALKScriptTokenType.Try)]
  [InlineData("catch", ALKScriptTokenType.Catch)]
  [InlineData("finally", ALKScriptTokenType.Finally)]
  [InlineData("throw", ALKScriptTokenType.Throw)]
  public void Tokenize_ExceptionHandlingKeyword_ReturnsExpectedToken(string source, ALKScriptTokenType expectedType)
  {
    var lexer = new ALKScriptLexer();

    var tokens = lexer.Tokenize(source).ToList();

    Assert.Equal(expectedType, tokens[0].Type);
    Assert.Equal(source, tokens[0].Lexeme);
  }

  [Fact]
  public void Tokenize_ThrowStatement_ReturnsExpectedTokenSequence()
  {
    var lexer = new ALKScriptLexer();

    var tokens = lexer.Tokenize("throw new InvalidAgeError(\"age cannot be negative\");").ToList();

    Assert.Equal(
      new[]
      {
        ALKScriptTokenType.Throw,
        ALKScriptTokenType.New,
        ALKScriptTokenType.Identifier,
        ALKScriptTokenType.LeftParen,
        ALKScriptTokenType.String,
        ALKScriptTokenType.RightParen,
        ALKScriptTokenType.Semicolon,
        ALKScriptTokenType.EndOfFile
      },
      tokens.ConvertAll(t => t.Type));
  }

  [Fact]
  public void Tokenize_ErrorSubclassDeclaration_ReturnsExpectedTokenSequence()
  {
    var lexer = new ALKScriptLexer();

    var tokens = lexer.Tokenize(
      "class InvalidAgeError extends Error {\n" +
      "  public new(string message) {\n" +
      "    base(message);\n" +
      "  }\n" +
      "}").ToList();

    Assert.Equal(
      new[]
      {
        ALKScriptTokenType.Class,
        ALKScriptTokenType.Identifier,
        ALKScriptTokenType.Extends,
        ALKScriptTokenType.Identifier,
        ALKScriptTokenType.LeftBrace,

        ALKScriptTokenType.Public,
        ALKScriptTokenType.New,
        ALKScriptTokenType.LeftParen,
        ALKScriptTokenType.StringKeyword,
        ALKScriptTokenType.Identifier,
        ALKScriptTokenType.RightParen,
        ALKScriptTokenType.LeftBrace,
        ALKScriptTokenType.Base,
        ALKScriptTokenType.LeftParen,
        ALKScriptTokenType.Identifier,
        ALKScriptTokenType.RightParen,
        ALKScriptTokenType.Semicolon,
        ALKScriptTokenType.RightBrace,

        ALKScriptTokenType.RightBrace,
        ALKScriptTokenType.EndOfFile
      },
      tokens.ConvertAll(t => t.Type));
  }

  [Fact]
  public void Tokenize_TryCatchFinally_ReturnsExpectedTokenSequence()
  {
    var lexer = new ALKScriptLexer();

    var tokens = lexer.Tokenize(
      "try {\n" +
      "  riskyOperation();\n" +
      "} catch (IOError e) {\n" +
      "  print(e.message);\n" +
      "} catch {\n" +
      "  print(\"failed\");\n" +
      "} finally {\n" +
      "  cleanup();\n" +
      "}").ToList();

    Assert.Equal(
      new[]
      {
        ALKScriptTokenType.Try,
        ALKScriptTokenType.LeftBrace,
        ALKScriptTokenType.Identifier,
        ALKScriptTokenType.LeftParen,
        ALKScriptTokenType.RightParen,
        ALKScriptTokenType.Semicolon,
        ALKScriptTokenType.RightBrace,

        ALKScriptTokenType.Catch,
        ALKScriptTokenType.LeftParen,
        ALKScriptTokenType.Identifier,
        ALKScriptTokenType.Identifier,
        ALKScriptTokenType.RightParen,
        ALKScriptTokenType.LeftBrace,
        ALKScriptTokenType.Identifier,
        ALKScriptTokenType.LeftParen,
        ALKScriptTokenType.Identifier,
        ALKScriptTokenType.Dot,
        ALKScriptTokenType.Identifier,
        ALKScriptTokenType.RightParen,
        ALKScriptTokenType.Semicolon,
        ALKScriptTokenType.RightBrace,

        ALKScriptTokenType.Catch,
        ALKScriptTokenType.LeftBrace,
        ALKScriptTokenType.Identifier,
        ALKScriptTokenType.LeftParen,
        ALKScriptTokenType.String,
        ALKScriptTokenType.RightParen,
        ALKScriptTokenType.Semicolon,
        ALKScriptTokenType.RightBrace,

        ALKScriptTokenType.Finally,
        ALKScriptTokenType.LeftBrace,
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
