using ALKScript.Interpreter.Common;
using ALKScript.Interpreter.Common.Token;
using ALKScript.Interpreter.Lexer;

namespace Tests.ALKScript.Interpreter.Lexer;

public class TemplateStringTests
{
  [Fact]
  public void Tokenize_TemplateStringWithoutInterpolation_ProducesSingleInterpolatedStringEndToken()
  {
    var lexer = new ALKScriptLexer();

    var tokens = lexer.Tokenize("`hello world`").ToList();

    Assert.Equal(ALKScriptTokenType.InterpolatedStringEnd, tokens[0].Type);
    Assert.Equal("hello world", tokens[0].Lexeme);
    Assert.Equal(ALKScriptTokenType.EndOfFile, tokens[1].Type);
  }

  [Fact]
  public void Tokenize_TemplateStringWithSingleInterpolation_ProducesStartExpressionAndEndTokens()
  {
    var lexer = new ALKScriptLexer();

    var tokens = lexer.Tokenize("`Hello, ${name}!`").ToList();

    Assert.Equal(
      new[]
      {
        ALKScriptTokenType.InterpolatedStringStart,
        ALKScriptTokenType.Identifier,
        ALKScriptTokenType.InterpolatedStringEnd,
        ALKScriptTokenType.EndOfFile
      },
      tokens.ConvertAll(t => t.Type));

    Assert.Equal("Hello, ", tokens[0].Lexeme);
    Assert.Equal("name", tokens[1].Lexeme);
    Assert.Equal("!", tokens[2].Lexeme);
  }

  [Fact]
  public void Tokenize_TemplateStringWithMultipleInterpolations_ProducesStartMidAndEndTokens()
  {
    var lexer = new ALKScriptLexer();

    var tokens = lexer.Tokenize("`${a} plus ${b} equals ${c}`").ToList();

    Assert.Equal(
      new[]
      {
        ALKScriptTokenType.InterpolatedStringStart,
        ALKScriptTokenType.Identifier,
        ALKScriptTokenType.InterpolatedStringMid,
        ALKScriptTokenType.Identifier,
        ALKScriptTokenType.InterpolatedStringMid,
        ALKScriptTokenType.Identifier,
        ALKScriptTokenType.InterpolatedStringEnd,
        ALKScriptTokenType.EndOfFile
      },
      tokens.ConvertAll(t => t.Type));

    Assert.Equal(string.Empty, tokens[0].Lexeme);
    Assert.Equal("a", tokens[1].Lexeme);
    Assert.Equal(" plus ", tokens[2].Lexeme);
    Assert.Equal("b", tokens[3].Lexeme);
    Assert.Equal(" equals ", tokens[4].Lexeme);
    Assert.Equal("c", tokens[5].Lexeme);
    Assert.Equal(string.Empty, tokens[6].Lexeme);
  }

  [Fact]
  public void Tokenize_TemplateStringWithExpressionInsideInterpolation_TokenizesExpressionNormally()
  {
    var lexer = new ALKScriptLexer();

    var tokens = lexer.Tokenize("`Total: ${a + b}`").ToList();

    Assert.Equal(
      new[]
      {
        ALKScriptTokenType.InterpolatedStringStart,
        ALKScriptTokenType.Identifier,
        ALKScriptTokenType.Plus,
        ALKScriptTokenType.Identifier,
        ALKScriptTokenType.InterpolatedStringEnd,
        ALKScriptTokenType.EndOfFile
      },
      tokens.ConvertAll(t => t.Type));
  }

  [Fact]
  public void Tokenize_BlockAfterTemplateString_StillProducesRightBraceToken()
  {
    // Ensures the '}' that closes a normal block is unaffected by interpolation tracking.
    var lexer = new ALKScriptLexer();

    var tokens = lexer.Tokenize("if (true) { `${x}`; }").ToList();

    Assert.Equal(
      new[]
      {
        ALKScriptTokenType.If,
        ALKScriptTokenType.LeftParen,
        ALKScriptTokenType.True,
        ALKScriptTokenType.RightParen,
        ALKScriptTokenType.LeftBrace,
        ALKScriptTokenType.InterpolatedStringStart,
        ALKScriptTokenType.Identifier,
        ALKScriptTokenType.InterpolatedStringEnd,
        ALKScriptTokenType.Semicolon,
        ALKScriptTokenType.RightBrace,
        ALKScriptTokenType.EndOfFile
      },
      tokens.ConvertAll(t => t.Type));
  }
}
