using ALKScript.Interpreter.Lexer;

namespace Tests.ALKScript.Interpreter.Lexer;

public class GenericsTests
{
  [Fact]
  public void Tokenize_GenericFunctionDeclaration_ReturnsExpectedTokenSequence()
  {
    var lexer = new FileLexer();

    var tokens = lexer.Tokenize("function<T> void process(T n) {\n}");

    Assert.Equal(
      new[]
      {
        TokenType.Function,
        TokenType.Less,
        TokenType.Identifier,
        TokenType.Greater,
        TokenType.VoidKeyword,
        TokenType.Identifier,
        TokenType.LeftParen,
        TokenType.Identifier,
        TokenType.Identifier,
        TokenType.RightParen,
        TokenType.LeftBrace,
        TokenType.RightBrace,
        TokenType.EndOfFile
      },
      tokens.ConvertAll(t => t.Type));
  }

  [Fact]
  public void Tokenize_GenericClassDeclaration_ReturnsExpectedTokenSequence()
  {
    var lexer = new FileLexer();

    var tokens = lexer.Tokenize("class Array<T> {\n  private T[] items = [];\n}");

    Assert.Equal(
      new[]
      {
        TokenType.Class,
        TokenType.Identifier,
        TokenType.Less,
        TokenType.Identifier,
        TokenType.Greater,
        TokenType.LeftBrace,
        TokenType.Private,
        TokenType.Identifier,
        TokenType.LeftBracket,
        TokenType.RightBracket,
        TokenType.Identifier,
        TokenType.Equal,
        TokenType.LeftBracket,
        TokenType.RightBracket,
        TokenType.Semicolon,
        TokenType.RightBrace,
        TokenType.EndOfFile
      },
      tokens.ConvertAll(t => t.Type));
  }

  [Fact]
  public void Tokenize_GenericInstantiation_ReturnsExpectedTokenSequence()
  {
    var lexer = new FileLexer();

    var tokens = lexer.Tokenize("var list = new Array<int>();");

    Assert.Equal(
      new[]
      {
        TokenType.Var,
        TokenType.Identifier,
        TokenType.Equal,
        TokenType.New,
        TokenType.Identifier,
        TokenType.Less,
        TokenType.IntKeyword,
        TokenType.Greater,
        TokenType.LeftParen,
        TokenType.RightParen,
        TokenType.Semicolon,
        TokenType.EndOfFile
      },
      tokens.ConvertAll(t => t.Type));
  }
}
