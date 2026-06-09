using ALKScript.Interpreter.Common;
using ALKScript.Interpreter.Common.Token;
using ALKScript.Interpreter.Lexer;

namespace Tests.ALKScript.Interpreter.Lexer;

public class BreakContinueTests
{
  [Theory]
  [InlineData("break", ALKScriptTokenType.Break)]
  [InlineData("continue", ALKScriptTokenType.Continue)]
  public void Tokenize_BreakOrContinueKeyword_ReturnsCorrectToken(string source, ALKScriptTokenType expectedType)
  {
    var lexer = new ALKScriptLexer();

    var tokens = lexer.Tokenize(source).ToList();

    Assert.Equal(expectedType, tokens[0].Type);
    Assert.Equal(source, tokens[0].Lexeme);
  }

  [Fact]
  public void Tokenize_BreakInsideWhileLoop_ReturnsExpectedTokenSequence()
  {
    var lexer = new ALKScriptLexer();

    var tokens = lexer.Tokenize("while (true) { break; }").ToList();

    Assert.Equal(
      new[]
      {
        ALKScriptTokenType.While,
        ALKScriptTokenType.LeftParen,
        ALKScriptTokenType.True,
        ALKScriptTokenType.RightParen,
        ALKScriptTokenType.LeftBrace,
        ALKScriptTokenType.Break,
        ALKScriptTokenType.Semicolon,
        ALKScriptTokenType.RightBrace,
        ALKScriptTokenType.EndOfFile
      },
      tokens.ConvertAll(t => t.Type));
  }

  [Fact]
  public void Tokenize_ContinueInsideForLoop_ReturnsExpectedTokenSequence()
  {
    var lexer = new ALKScriptLexer();

    var tokens = lexer.Tokenize("for (var i = 0; i < 10; i = i + 1) { continue; }").ToList();

    Assert.Contains(ALKScriptTokenType.Continue, tokens.ConvertAll(t => t.Type));
    Assert.Contains(ALKScriptTokenType.Semicolon, tokens.ConvertAll(t => t.Type));
  }

  [Theory]
  [InlineData("breakpoint")]   // identifier starting with "break"
  [InlineData("continues")]    // identifier starting with "continue"
  public void Tokenize_IdentifierWithKeywordPrefix_IsNotTokenizedAsKeyword(string source)
  {
    var lexer = new ALKScriptLexer();

    var tokens = lexer.Tokenize(source).ToList();

    Assert.Equal(ALKScriptTokenType.Identifier, tokens[0].Type);
    Assert.Equal(source, tokens[0].Lexeme);
  }
}
