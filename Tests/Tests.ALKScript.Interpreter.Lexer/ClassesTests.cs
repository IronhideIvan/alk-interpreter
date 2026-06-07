using ALKScript.Interpreter.Lexer;

namespace Tests.ALKScript.Interpreter.Lexer;

public class ClassesTests
{
  [Theory]
  [InlineData("class", TokenType.Class)]
  [InlineData("new", TokenType.New)]
  [InlineData("this", TokenType.This)]
  [InlineData("base", TokenType.Base)]
  [InlineData("extends", TokenType.Extends)]
  [InlineData("public", TokenType.Public)]
  [InlineData("protected", TokenType.Protected)]
  [InlineData("private", TokenType.Private)]
  [InlineData("virtual", TokenType.Virtual)]
  [InlineData("abstract", TokenType.Abstract)]
  [InlineData("override", TokenType.Override)]
  public void Tokenize_ClassKeyword_ReturnsExpectedToken(string source, TokenType expectedType)
  {
    var lexer = new FileLexer();

    var tokens = lexer.Tokenize(source);

    Assert.Equal(expectedType, tokens[0].Type);
    Assert.Equal(source, tokens[0].Lexeme);
  }

  [Fact]
  public void Tokenize_ClassDeclarationWithExtends_ReturnsExpectedTokenSequence()
  {
    var lexer = new FileLexer();

    var tokens = lexer.Tokenize("class Student extends Person {\n}");

    Assert.Equal(
      new[]
      {
        TokenType.Class,
        TokenType.Identifier,
        TokenType.Extends,
        TokenType.Identifier,
        TokenType.LeftBrace,
        TokenType.RightBrace,
        TokenType.EndOfFile
      },
      tokens.ConvertAll(t => t.Type));
  }

  [Fact]
  public void Tokenize_AbstractClassDeclaration_ReturnsExpectedTokenSequence()
  {
    var lexer = new FileLexer();

    var tokens = lexer.Tokenize("abstract class Person {\n}");

    Assert.Equal(
      new[]
      {
        TokenType.Abstract,
        TokenType.Class,
        TokenType.Identifier,
        TokenType.LeftBrace,
        TokenType.RightBrace,
        TokenType.EndOfFile
      },
      tokens.ConvertAll(t => t.Type));
  }

  [Fact]
  public void Tokenize_VirtualMethodDeclaration_ReturnsExpectedTokenSequence()
  {
    var lexer = new FileLexer();

    var tokens = lexer.Tokenize("public virtual function string greet() {\n  return \"Hello\";\n}");

    Assert.Equal(
      new[]
      {
        TokenType.Public,
        TokenType.Virtual,
        TokenType.Function,
        TokenType.StringKeyword,
        TokenType.Identifier,
        TokenType.LeftParen,
        TokenType.RightParen,
        TokenType.LeftBrace,
        TokenType.Return,
        TokenType.String,
        TokenType.Semicolon,
        TokenType.RightBrace,
        TokenType.EndOfFile
      },
      tokens.ConvertAll(t => t.Type));
  }

  [Fact]
  public void Tokenize_OverrideMethodDeclaration_ReturnsExpectedTokenSequence()
  {
    var lexer = new FileLexer();

    var tokens = lexer.Tokenize("public override function string greet() {\n  return base.greet();\n}");

    Assert.Equal(TokenType.Override, tokens[1].Type);
    Assert.Equal(TokenType.Base, tokens[9].Type);
    Assert.Equal(TokenType.Dot, tokens[10].Type);
  }

  [Fact]
  public void Tokenize_NewExpression_ReturnsExpectedTokenSequence()
  {
    var lexer = new FileLexer();

    var tokens = lexer.Tokenize("var person = new Person();");

    Assert.Equal(
      new[]
      {
        TokenType.Var,
        TokenType.Identifier,
        TokenType.Equal,
        TokenType.New,
        TokenType.Identifier,
        TokenType.LeftParen,
        TokenType.RightParen,
        TokenType.Semicolon,
        TokenType.EndOfFile
      },
      tokens.ConvertAll(t => t.Type));
  }
}
