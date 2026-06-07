using ALKScript.Interpreter.Common;
using ALKScript.Interpreter.Common.Token;
using ALKScript.Interpreter.Lexer;

namespace Tests.ALKScript.Interpreter.Lexer;

public class GenericsTests
{
  [Fact]
  public void Tokenize_GenericFunctionDeclaration_ReturnsExpectedTokenSequence()
  {
    var lexer = new ALKScriptLexer();

    var tokens = lexer.Tokenize("function<T> void process(T n) {\n}").ToList();

    Assert.Equal(
      new[]
      {
        ALKScriptTokenType.Function,
        ALKScriptTokenType.Less,
        ALKScriptTokenType.Identifier,
        ALKScriptTokenType.Greater,
        ALKScriptTokenType.VoidKeyword,
        ALKScriptTokenType.Identifier,
        ALKScriptTokenType.LeftParen,
        ALKScriptTokenType.Identifier,
        ALKScriptTokenType.Identifier,
        ALKScriptTokenType.RightParen,
        ALKScriptTokenType.LeftBrace,
        ALKScriptTokenType.RightBrace,
        ALKScriptTokenType.EndOfFile
      },
      tokens.ConvertAll(t => t.Type));
  }

  [Fact]
  public void Tokenize_GenericClassDeclaration_ReturnsExpectedTokenSequence()
  {
    var lexer = new ALKScriptLexer();

    var tokens = lexer.Tokenize("class Array<T> {\n  private T[] items = [];\n}").ToList();

    Assert.Equal(
      new[]
      {
        ALKScriptTokenType.Class,
        ALKScriptTokenType.Identifier,
        ALKScriptTokenType.Less,
        ALKScriptTokenType.Identifier,
        ALKScriptTokenType.Greater,
        ALKScriptTokenType.LeftBrace,
        ALKScriptTokenType.Private,
        ALKScriptTokenType.Identifier,
        ALKScriptTokenType.LeftBracket,
        ALKScriptTokenType.RightBracket,
        ALKScriptTokenType.Identifier,
        ALKScriptTokenType.Equal,
        ALKScriptTokenType.LeftBracket,
        ALKScriptTokenType.RightBracket,
        ALKScriptTokenType.Semicolon,
        ALKScriptTokenType.RightBrace,
        ALKScriptTokenType.EndOfFile
      },
      tokens.ConvertAll(t => t.Type));
  }

  [Fact]
  public void Tokenize_GenericInstantiation_ReturnsExpectedTokenSequence()
  {
    var lexer = new ALKScriptLexer();

    var tokens = lexer.Tokenize("var list = new Array<int>();").ToList();

    Assert.Equal(
      new[]
      {
        ALKScriptTokenType.Var,
        ALKScriptTokenType.Identifier,
        ALKScriptTokenType.Equal,
        ALKScriptTokenType.New,
        ALKScriptTokenType.Identifier,
        ALKScriptTokenType.Less,
        ALKScriptTokenType.IntKeyword,
        ALKScriptTokenType.Greater,
        ALKScriptTokenType.LeftParen,
        ALKScriptTokenType.RightParen,
        ALKScriptTokenType.Semicolon,
        ALKScriptTokenType.EndOfFile
      },
      tokens.ConvertAll(t => t.Type));
  }
}
