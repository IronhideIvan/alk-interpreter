using ALKScript.Interpreter.Lexer;

namespace Tests.ALKScript.Interpreter.Lexer;

public class FunctionsTests
{
  [Theory]
  [InlineData("if", ALKScriptTokenType.If)]
  [InlineData("else", ALKScriptTokenType.Else)]
  [InlineData("while", ALKScriptTokenType.While)]
  [InlineData("for", ALKScriptTokenType.For)]
  [InlineData("function", ALKScriptTokenType.Function)]
  [InlineData("return", ALKScriptTokenType.Return)]
  [InlineData("true", ALKScriptTokenType.True)]
  [InlineData("false", ALKScriptTokenType.False)]
  [InlineData("null", ALKScriptTokenType.Null)]
  public void Tokenize_Keyword_ReturnsKeywordToken(string source, ALKScriptTokenType expectedType)
  {
    var lexer = new ALKScriptLexer();

    var tokens = lexer.Tokenize(source);

    Assert.Equal(expectedType, tokens[0].Type);
    Assert.Equal(source, tokens[0].Lexeme);
  }

  [Fact]
  public void Tokenize_FunctionDeclarationWithReturnType_ReturnsExpectedTokenSequence()
  {
    var lexer = new ALKScriptLexer();

    var tokens = lexer.Tokenize("function int add(int a, int b) {\n  return a + b;\n}");

    Assert.Equal(
      new[]
      {
        ALKScriptTokenType.Function,
        ALKScriptTokenType.IntKeyword,
        ALKScriptTokenType.Identifier,
        ALKScriptTokenType.LeftParen,
        ALKScriptTokenType.IntKeyword,
        ALKScriptTokenType.Identifier,
        ALKScriptTokenType.Comma,
        ALKScriptTokenType.IntKeyword,
        ALKScriptTokenType.Identifier,
        ALKScriptTokenType.RightParen,
        ALKScriptTokenType.LeftBrace,
        ALKScriptTokenType.Return,
        ALKScriptTokenType.Identifier,
        ALKScriptTokenType.Plus,
        ALKScriptTokenType.Identifier,
        ALKScriptTokenType.Semicolon,
        ALKScriptTokenType.RightBrace,
        ALKScriptTokenType.EndOfFile
      },
      tokens.ConvertAll(t => t.Type));
  }

  [Fact]
  public void Tokenize_VoidFunctionDeclaration_ReturnsExpectedTokenSequence()
  {
    var lexer = new ALKScriptLexer();

    var tokens = lexer.Tokenize("function void log(string message) {\n  print(message);\n}");

    Assert.Equal(
      new[]
      {
        ALKScriptTokenType.Function,
        ALKScriptTokenType.VoidKeyword,
        ALKScriptTokenType.Identifier,
        ALKScriptTokenType.LeftParen,
        ALKScriptTokenType.StringKeyword,
        ALKScriptTokenType.Identifier,
        ALKScriptTokenType.RightParen,
        ALKScriptTokenType.LeftBrace,
        ALKScriptTokenType.Identifier,
        ALKScriptTokenType.LeftParen,
        ALKScriptTokenType.Identifier,
        ALKScriptTokenType.RightParen,
        ALKScriptTokenType.Semicolon,
        ALKScriptTokenType.RightBrace,
        ALKScriptTokenType.EndOfFile
      },
      tokens.ConvertAll(t => t.Type));
  }

  [Fact]
  public void Tokenize_IfElseStatement_ReturnsExpectedTokenSequence()
  {
    var lexer = new ALKScriptLexer();

    var tokens = lexer.Tokenize("if (a < b) { return a; } else { return b; }");

    Assert.Equal(ALKScriptTokenType.If, tokens[0].Type);
    Assert.Equal(ALKScriptTokenType.Else, tokens[11].Type);
  }
}
