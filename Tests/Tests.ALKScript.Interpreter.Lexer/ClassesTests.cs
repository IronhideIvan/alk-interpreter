using ALKScript.Interpreter.Lexer;

namespace Tests.ALKScript.Interpreter.Lexer;

public class ClassesTests
{
  [Theory]
  [InlineData("class", ALKScriptTokenType.Class)]
  [InlineData("new", ALKScriptTokenType.New)]
  [InlineData("this", ALKScriptTokenType.This)]
  [InlineData("base", ALKScriptTokenType.Base)]
  [InlineData("extends", ALKScriptTokenType.Extends)]
  [InlineData("public", ALKScriptTokenType.Public)]
  [InlineData("protected", ALKScriptTokenType.Protected)]
  [InlineData("private", ALKScriptTokenType.Private)]
  [InlineData("virtual", ALKScriptTokenType.Virtual)]
  [InlineData("abstract", ALKScriptTokenType.Abstract)]
  [InlineData("override", ALKScriptTokenType.Override)]
  public void Tokenize_ClassKeyword_ReturnsExpectedToken(string source, ALKScriptTokenType expectedType)
  {
    var lexer = new ALKScriptLexer();

    var tokens = lexer.Tokenize(source);

    Assert.Equal(expectedType, tokens[0].Type);
    Assert.Equal(source, tokens[0].Lexeme);
  }

  [Fact]
  public void Tokenize_ClassDeclarationWithExtends_ReturnsExpectedTokenSequence()
  {
    var lexer = new ALKScriptLexer();

    var tokens = lexer.Tokenize("class Student extends Person {\n}");

    Assert.Equal(
      new[]
      {
        ALKScriptTokenType.Class,
        ALKScriptTokenType.Identifier,
        ALKScriptTokenType.Extends,
        ALKScriptTokenType.Identifier,
        ALKScriptTokenType.LeftBrace,
        ALKScriptTokenType.RightBrace,
        ALKScriptTokenType.EndOfFile
      },
      tokens.ConvertAll(t => t.Type));
  }

  [Fact]
  public void Tokenize_AbstractClassDeclaration_ReturnsExpectedTokenSequence()
  {
    var lexer = new ALKScriptLexer();

    var tokens = lexer.Tokenize("abstract class Person {\n}");

    Assert.Equal(
      new[]
      {
        ALKScriptTokenType.Abstract,
        ALKScriptTokenType.Class,
        ALKScriptTokenType.Identifier,
        ALKScriptTokenType.LeftBrace,
        ALKScriptTokenType.RightBrace,
        ALKScriptTokenType.EndOfFile
      },
      tokens.ConvertAll(t => t.Type));
  }

  [Fact]
  public void Tokenize_VirtualMethodDeclaration_ReturnsExpectedTokenSequence()
  {
    var lexer = new ALKScriptLexer();

    var tokens = lexer.Tokenize("public virtual function string greet() {\n  return \"Hello\";\n}");

    Assert.Equal(
      new[]
      {
        ALKScriptTokenType.Public,
        ALKScriptTokenType.Virtual,
        ALKScriptTokenType.Function,
        ALKScriptTokenType.StringKeyword,
        ALKScriptTokenType.Identifier,
        ALKScriptTokenType.LeftParen,
        ALKScriptTokenType.RightParen,
        ALKScriptTokenType.LeftBrace,
        ALKScriptTokenType.Return,
        ALKScriptTokenType.String,
        ALKScriptTokenType.Semicolon,
        ALKScriptTokenType.RightBrace,
        ALKScriptTokenType.EndOfFile
      },
      tokens.ConvertAll(t => t.Type));
  }

  [Fact]
  public void Tokenize_OverrideMethodDeclaration_ReturnsExpectedTokenSequence()
  {
    var lexer = new ALKScriptLexer();

    var tokens = lexer.Tokenize("public override function string greet() {\n  return base.greet();\n}");

    Assert.Equal(ALKScriptTokenType.Override, tokens[1].Type);
    Assert.Equal(ALKScriptTokenType.Base, tokens[9].Type);
    Assert.Equal(ALKScriptTokenType.Dot, tokens[10].Type);
  }

  [Fact]
  public void Tokenize_NewExpression_ReturnsExpectedTokenSequence()
  {
    var lexer = new ALKScriptLexer();

    var tokens = lexer.Tokenize("var person = new Person();");

    Assert.Equal(
      new[]
      {
        ALKScriptTokenType.Var,
        ALKScriptTokenType.Identifier,
        ALKScriptTokenType.Equal,
        ALKScriptTokenType.New,
        ALKScriptTokenType.Identifier,
        ALKScriptTokenType.LeftParen,
        ALKScriptTokenType.RightParen,
        ALKScriptTokenType.Semicolon,
        ALKScriptTokenType.EndOfFile
      },
      tokens.ConvertAll(t => t.Type));
  }
}
