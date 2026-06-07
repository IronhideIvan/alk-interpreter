using ALKScript.Interpreter.Lexer;

namespace Tests.ALKScript.Interpreter.Lexer;

public class TypesAndVariablesTests
{
  [Theory]
  [InlineData("int", TokenType.IntKeyword)]
  [InlineData("long", TokenType.LongKeyword)]
  [InlineData("float", TokenType.FloatKeyword)]
  [InlineData("string", TokenType.StringKeyword)]
  [InlineData("bool", TokenType.BoolKeyword)]
  [InlineData("void", TokenType.VoidKeyword)]
  [InlineData("var", TokenType.Var)]
  public void Tokenize_TypeOrVariableKeyword_ReturnsExpectedToken(string source, TokenType expectedType)
  {
    var lexer = new FileLexer();

    var tokens = lexer.Tokenize(source);

    Assert.Equal(expectedType, tokens[0].Type);
    Assert.Equal(source, tokens[0].Lexeme);
  }

  [Fact]
  public void Tokenize_ExplicitlyTypedDeclaration_ReturnsExpectedTokenSequence()
  {
    var lexer = new FileLexer();

    var tokens = lexer.Tokenize("int num = 1;");

    Assert.Equal(
      new[]
      {
        TokenType.IntKeyword,
        TokenType.Identifier,
        TokenType.Equal,
        TokenType.Number,
        TokenType.Semicolon,
        TokenType.EndOfFile
      },
      tokens.ConvertAll(t => t.Type));
  }

  [Fact]
  public void Tokenize_InferredDeclaration_ReturnsExpectedTokenSequence()
  {
    var lexer = new FileLexer();

    var tokens = lexer.Tokenize("var num = 1;");

    Assert.Equal(
      new[]
      {
        TokenType.Var,
        TokenType.Identifier,
        TokenType.Equal,
        TokenType.Number,
        TokenType.Semicolon,
        TokenType.EndOfFile
      },
      tokens.ConvertAll(t => t.Type));
  }

  [Fact]
  public void Tokenize_LongLiteral_ReturnsNumberTokenWithSuffix()
  {
    var lexer = new FileLexer();

    var tokens = lexer.Tokenize("long big = 42L;");

    Assert.Equal(TokenType.LongKeyword, tokens[0].Type);
    Assert.Equal(TokenType.Number, tokens[3].Type);
    Assert.Equal("42L", tokens[3].Lexeme);
  }

  [Fact]
  public void Tokenize_ArrayDeclaration_ReturnsExpectedTokenSequence()
  {
    var lexer = new FileLexer();

    var tokens = lexer.Tokenize("int[] numArr = [1, 2, 3, 4];");

    Assert.Equal(
      new[]
      {
        TokenType.IntKeyword,
        TokenType.LeftBracket,
        TokenType.RightBracket,
        TokenType.Identifier,
        TokenType.Equal,
        TokenType.LeftBracket,
        TokenType.Number,
        TokenType.Comma,
        TokenType.Number,
        TokenType.Comma,
        TokenType.Number,
        TokenType.Comma,
        TokenType.Number,
        TokenType.RightBracket,
        TokenType.Semicolon,
        TokenType.EndOfFile
      },
      tokens.ConvertAll(t => t.Type));
  }
}
