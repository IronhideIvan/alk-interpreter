using ALKScript.Interpreter.Lexer;

namespace Tests.ALKScript.Interpreter.Lexer;

public class ModulesTests
{
  [Theory]
  [InlineData("import", TokenType.Import)]
  [InlineData("export", TokenType.Export)]
  [InlineData("from", TokenType.From)]
  [InlineData("as", TokenType.As)]
  public void Tokenize_ModuleKeyword_ReturnsExpectedToken(string source, TokenType expectedType)
  {
    var lexer = new FileLexer();

    var tokens = lexer.Tokenize(source);

    Assert.Equal(expectedType, tokens[0].Type);
    Assert.Equal(source, tokens[0].Lexeme);
  }

  [Fact]
  public void Tokenize_ImportFromRelativePath_ReturnsExpectedTokenSequence()
  {
    var lexer = new FileLexer();

    var tokens = lexer.Tokenize("import { Person } from \"./person\";");

    Assert.Equal(
      new[]
      {
        TokenType.Import,
        TokenType.LeftBrace,
        TokenType.Identifier,
        TokenType.RightBrace,
        TokenType.From,
        TokenType.String,
        TokenType.Semicolon,
        TokenType.EndOfFile
      },
      tokens.ConvertAll(t => t.Type));
  }

  [Fact]
  public void Tokenize_ImportCoreModuleWithAlias_ReturnsExpectedTokenSequence()
  {
    var lexer = new FileLexer();

    var tokens = lexer.Tokenize("import { Date as SystemDate } from \"datetime\";");

    Assert.Equal(
      new[]
      {
        TokenType.Import,
        TokenType.LeftBrace,
        TokenType.Identifier,
        TokenType.As,
        TokenType.Identifier,
        TokenType.RightBrace,
        TokenType.From,
        TokenType.String,
        TokenType.Semicolon,
        TokenType.EndOfFile
      },
      tokens.ConvertAll(t => t.Type));
  }

  [Fact]
  public void Tokenize_ExportDeclaration_ReturnsExpectedTokenSequence()
  {
    var lexer = new FileLexer();

    var tokens = lexer.Tokenize("export class Person {\n}");

    Assert.Equal(
      new[]
      {
        TokenType.Export,
        TokenType.Class,
        TokenType.Identifier,
        TokenType.LeftBrace,
        TokenType.RightBrace,
        TokenType.EndOfFile
      },
      tokens.ConvertAll(t => t.Type));
  }
}
