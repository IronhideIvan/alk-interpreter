using ALKScript.Interpreter.Common;
using ALKScript.Interpreter.Common.Token;
using ALKScript.Interpreter.Lexer;

namespace Tests.ALKScript.Interpreter.Lexer;

public class OperatorsAndPunctuationTests
{
  [Theory]
  [InlineData("+",  ALKScriptTokenType.Plus)]
  [InlineData("++", ALKScriptTokenType.PlusPlus)]
  [InlineData("-",  ALKScriptTokenType.Minus)]
  [InlineData("--", ALKScriptTokenType.MinusMinus)]
  [InlineData("*", ALKScriptTokenType.Star)]
  [InlineData("/", ALKScriptTokenType.Slash)]
  [InlineData("%", ALKScriptTokenType.Percent)]
  [InlineData("=", ALKScriptTokenType.Equal)]
  [InlineData("==", ALKScriptTokenType.EqualEqual)]
  [InlineData("!", ALKScriptTokenType.Bang)]
  [InlineData("!=", ALKScriptTokenType.BangEqual)]
  [InlineData("<", ALKScriptTokenType.Less)]
  [InlineData("<=", ALKScriptTokenType.LessEqual)]
  [InlineData(">", ALKScriptTokenType.Greater)]
  [InlineData(">=", ALKScriptTokenType.GreaterEqual)]
  [InlineData("&&", ALKScriptTokenType.AmpAmp)]
  [InlineData("||", ALKScriptTokenType.PipePipe)]
  [InlineData("(", ALKScriptTokenType.LeftParen)]
  [InlineData(")", ALKScriptTokenType.RightParen)]
  [InlineData("{", ALKScriptTokenType.LeftBrace)]
  [InlineData("}", ALKScriptTokenType.RightBrace)]
  [InlineData("[", ALKScriptTokenType.LeftBracket)]
  [InlineData("]", ALKScriptTokenType.RightBracket)]
  [InlineData(",", ALKScriptTokenType.Comma)]
  [InlineData(";", ALKScriptTokenType.Semicolon)]
  [InlineData(":", ALKScriptTokenType.Colon)]
  [InlineData(".", ALKScriptTokenType.Dot)]
  [InlineData("?",  ALKScriptTokenType.Question)]
  [InlineData("??", ALKScriptTokenType.QuestionQuestion)]
  [InlineData("+=", ALKScriptTokenType.PlusEqual)]
  [InlineData("-=", ALKScriptTokenType.MinusEqual)]
  [InlineData("*=", ALKScriptTokenType.StarEqual)]
  [InlineData("/=", ALKScriptTokenType.SlashEqual)]
  [InlineData("%=", ALKScriptTokenType.PercentEqual)]
  [InlineData("&", ALKScriptTokenType.Amp)]
  [InlineData("&=", ALKScriptTokenType.AmpEqual)]
  [InlineData("|", ALKScriptTokenType.Pipe)]
  [InlineData("|=", ALKScriptTokenType.PipeEqual)]
  [InlineData("^", ALKScriptTokenType.Caret)]
  [InlineData("^=", ALKScriptTokenType.CaretEqual)]
  [InlineData("~", ALKScriptTokenType.Tilde)]
  [InlineData("<<", ALKScriptTokenType.LessLess)]
  [InlineData("<<=", ALKScriptTokenType.LessLessEqual)]
  [InlineData(">>", ALKScriptTokenType.GreaterGreater)]
  [InlineData(">>=", ALKScriptTokenType.GreaterGreaterEqual)]
  [InlineData("=>", ALKScriptTokenType.EqualGreater)]
  public void Tokenize_OperatorOrPunctuation_ReturnsExpectedToken(string source, ALKScriptTokenType expectedType)
  {
    var lexer = new ALKScriptLexer();

    var tokens = lexer.Tokenize(source).ToList();

    Assert.Equal(expectedType, tokens[0].Type);
    Assert.Equal(source, tokens[0].Lexeme);
  }

  [Fact]
  public void Tokenize_PlusFollowedByPlus_ProducesPlusPlusNotTwoPluses()
  {
    // "x++" must lex as Identifier + PlusPlus, not Identifier + Plus + Plus.
    var lexer = new ALKScriptLexer();
    var tokens = lexer.Tokenize("x++").ToList();
    Assert.Equal(ALKScriptTokenType.Identifier, tokens[0].Type);
    Assert.Equal(ALKScriptTokenType.PlusPlus,   tokens[1].Type);
    Assert.Equal("++", tokens[1].Lexeme);
  }

  [Fact]
  public void Tokenize_MinusFollowedByMinus_ProducesMinusMinusNotTwoMinuses()
  {
    var lexer = new ALKScriptLexer();
    var tokens = lexer.Tokenize("x--").ToList();
    Assert.Equal(ALKScriptTokenType.Identifier,  tokens[0].Type);
    Assert.Equal(ALKScriptTokenType.MinusMinus,  tokens[1].Type);
    Assert.Equal("--", tokens[1].Lexeme);
  }

  [Fact]
  public void Tokenize_PlusSpacedFromPlus_ProducesTwoSeparatePlusTokens()
  {
    // "x + +y" must NOT lex the two '+' characters as '++'.
    var lexer = new ALKScriptLexer();
    var tokens = lexer.Tokenize("x + +y").ToList();
    Assert.Equal(ALKScriptTokenType.Identifier, tokens[0].Type);
    Assert.Equal(ALKScriptTokenType.Plus,       tokens[1].Type);
    Assert.Equal(ALKScriptTokenType.Plus,       tokens[2].Type);
    Assert.Equal(ALKScriptTokenType.Identifier, tokens[3].Type);
  }

  [Fact]
  public void Tokenize_NullableTypeAnnotation_ReturnsQuestionToken()
  {
    var lexer = new ALKScriptLexer();

    var tokens = lexer.Tokenize("string? name = null;").ToList();

    Assert.Equal(
      new[]
      {
        ALKScriptTokenType.StringKeyword,
        ALKScriptTokenType.Question,
        ALKScriptTokenType.Identifier,
        ALKScriptTokenType.Equal,
        ALKScriptTokenType.Null,
        ALKScriptTokenType.Semicolon,
        ALKScriptTokenType.EndOfFile
      },
      tokens.ConvertAll(t => t.Type));
  }

  [Fact]
  public void Tokenize_QuestionDot_ProducesQuestionDotToken()
  {
    var lexer = new ALKScriptLexer();
    var tokens = lexer.Tokenize("obj?.name").ToList();
    Assert.Equal(ALKScriptTokenType.Identifier,   tokens[0].Type);
    Assert.Equal(ALKScriptTokenType.QuestionDot,  tokens[1].Type);
    Assert.Equal("?.",                            tokens[1].Lexeme);
    Assert.Equal(ALKScriptTokenType.Identifier,   tokens[2].Type);
  }

  [Fact]
  public void Tokenize_QuestionFollowedByIdentifier_ProducesSeparateQuestionAndIdentifier()
  {
    // "x? y" is nullable type annotation — '?' + ' ' + identifier — not '?.'.
    var lexer = new ALKScriptLexer();
    var tokens = lexer.Tokenize("x? y").ToList();
    Assert.Equal(ALKScriptTokenType.Identifier, tokens[0].Type);
    Assert.Equal(ALKScriptTokenType.Question,   tokens[1].Type);
    Assert.Equal(ALKScriptTokenType.Identifier, tokens[2].Type);
  }

  [Theory]
  [InlineData("foreach", ALKScriptTokenType.Foreach)]
  [InlineData("in",      ALKScriptTokenType.In)]
  [InlineData("do",      ALKScriptTokenType.Do)]
  public void Tokenize_NewKeyword_ReturnsExpectedTokenType(string source, ALKScriptTokenType expected)
  {
    var lexer = new ALKScriptLexer();
    var tokens = lexer.Tokenize(source).ToList();
    Assert.Equal(expected, tokens[0].Type);
    Assert.Equal(source,   tokens[0].Lexeme);
  }

  [Fact]
  public void Tokenize_CompoundAssignment_ProducesCorrectTokenTypes()
  {
    // "x += 1; x -= 1; x *= 2; x /= 2; x %= 3;"
    var lexer = new ALKScriptLexer();
    var tokens = lexer.Tokenize("x += 1").ToList();
    Assert.Equal(ALKScriptTokenType.Identifier, tokens[0].Type);
    Assert.Equal(ALKScriptTokenType.PlusEqual,  tokens[1].Type);
    Assert.Equal("+=",                          tokens[1].Lexeme);
    Assert.Equal(ALKScriptTokenType.Number,     tokens[2].Type);
  }

  [Fact]
  public void Tokenize_SlashEqual_ProducesSlashEqualTokenNotSlashThenEqual()
  {
    var lexer = new ALKScriptLexer();
    var tokens = lexer.Tokenize("x /= 2").ToList();
    Assert.Equal(ALKScriptTokenType.Identifier, tokens[0].Type);
    Assert.Equal(ALKScriptTokenType.SlashEqual, tokens[1].Type);
    Assert.Equal("/=",                          tokens[1].Lexeme);
  }
}
