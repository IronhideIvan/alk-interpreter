using ALKScript.Interpreter.Common;
using ALKScript.Interpreter.Common.Token;
using ALKScript.Interpreter.Lexer;

namespace Tests.ALKScript.Interpreter.Lexer;

public class TypesAndVariablesTests
{
  [Theory]
  [InlineData("int", ALKScriptTokenType.IntKeyword)]
  [InlineData("long", ALKScriptTokenType.LongKeyword)]
  [InlineData("float", ALKScriptTokenType.FloatKeyword)]
  [InlineData("string", ALKScriptTokenType.StringKeyword)]
  [InlineData("bool", ALKScriptTokenType.BoolKeyword)]
  [InlineData("void", ALKScriptTokenType.VoidKeyword)]
  [InlineData("var", ALKScriptTokenType.Var)]
  [InlineData("lambda", ALKScriptTokenType.Lambda)]
  public void Tokenize_TypeOrVariableKeyword_ReturnsExpectedToken(string source, ALKScriptTokenType expectedType)
  {
    var lexer = new ALKScriptLexer();

    var tokens = lexer.Tokenize(source).ToList();

    Assert.Equal(expectedType, tokens[0].Type);
    Assert.Equal(source, tokens[0].Lexeme);
  }

  [Fact]
  public void Tokenize_ExplicitlyTypedDeclaration_ReturnsExpectedTokenSequence()
  {
    var lexer = new ALKScriptLexer();

    var tokens = lexer.Tokenize("int num = 1;").ToList();

    Assert.Equal(
      new[]
      {
        ALKScriptTokenType.IntKeyword,
        ALKScriptTokenType.Identifier,
        ALKScriptTokenType.Equal,
        ALKScriptTokenType.Number,
        ALKScriptTokenType.Semicolon,
        ALKScriptTokenType.EndOfFile
      },
      tokens.ConvertAll(t => t.Type));
  }

  [Fact]
  public void Tokenize_InferredDeclaration_ReturnsExpectedTokenSequence()
  {
    var lexer = new ALKScriptLexer();

    var tokens = lexer.Tokenize("var num = 1;").ToList();

    Assert.Equal(
      new[]
      {
        ALKScriptTokenType.Var,
        ALKScriptTokenType.Identifier,
        ALKScriptTokenType.Equal,
        ALKScriptTokenType.Number,
        ALKScriptTokenType.Semicolon,
        ALKScriptTokenType.EndOfFile
      },
      tokens.ConvertAll(t => t.Type));
  }

  [Fact]
  public void Tokenize_LongLiteral_ReturnsNumberTokenWithSuffix()
  {
    var lexer = new ALKScriptLexer();

    var tokens = lexer.Tokenize("long big = 42L;").ToList();

    Assert.Equal(ALKScriptTokenType.LongKeyword, tokens[0].Type);
    Assert.Equal(ALKScriptTokenType.Number, tokens[3].Type);
    Assert.Equal("42L", tokens[3].Lexeme);
  }

  [Fact]
  public void Tokenize_ArrayDeclaration_ReturnsExpectedTokenSequence()
  {
    var lexer = new ALKScriptLexer();

    var tokens = lexer.Tokenize("int[] numArr = [1, 2, 3, 4];").ToList();

    Assert.Equal(
      new[]
      {
        ALKScriptTokenType.IntKeyword,
        ALKScriptTokenType.LeftBracket,
        ALKScriptTokenType.RightBracket,
        ALKScriptTokenType.Identifier,
        ALKScriptTokenType.Equal,
        ALKScriptTokenType.LeftBracket,
        ALKScriptTokenType.Number,
        ALKScriptTokenType.Comma,
        ALKScriptTokenType.Number,
        ALKScriptTokenType.Comma,
        ALKScriptTokenType.Number,
        ALKScriptTokenType.Comma,
        ALKScriptTokenType.Number,
        ALKScriptTokenType.RightBracket,
        ALKScriptTokenType.Semicolon,
        ALKScriptTokenType.EndOfFile
      },
      tokens.ConvertAll(t => t.Type));
  }
}
