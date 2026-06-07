using ALKScript.Interpreter.Lexer;

namespace Tests.ALKScript.Interpreter.Lexer;

public class ModulesTests
{
  [Theory]
  [InlineData("import", ALKScriptTokenType.Import)]
  [InlineData("export", ALKScriptTokenType.Export)]
  [InlineData("from", ALKScriptTokenType.From)]
  [InlineData("as", ALKScriptTokenType.As)]
  public void Tokenize_ModuleKeyword_ReturnsExpectedToken(string source, ALKScriptTokenType expectedType)
  {
    var lexer = new ALKScriptLexer();

    var tokens = lexer.Tokenize(source);

    Assert.Equal(expectedType, tokens[0].Type);
    Assert.Equal(source, tokens[0].Lexeme);
  }

  [Fact]
  public void Tokenize_ImportFromRelativePath_ReturnsExpectedTokenSequence()
  {
    var lexer = new ALKScriptLexer();

    var tokens = lexer.Tokenize("import { Person } from \"./person\";");

    Assert.Equal(
      new[]
      {
        ALKScriptTokenType.Import,
        ALKScriptTokenType.LeftBrace,
        ALKScriptTokenType.Identifier,
        ALKScriptTokenType.RightBrace,
        ALKScriptTokenType.From,
        ALKScriptTokenType.String,
        ALKScriptTokenType.Semicolon,
        ALKScriptTokenType.EndOfFile
      },
      tokens.ConvertAll(t => t.Type));
  }

  [Fact]
  public void Tokenize_ImportCoreModuleWithAlias_ReturnsExpectedTokenSequence()
  {
    var lexer = new ALKScriptLexer();

    var tokens = lexer.Tokenize("import { Date as SystemDate } from \"datetime\";");

    Assert.Equal(
      new[]
      {
        ALKScriptTokenType.Import,
        ALKScriptTokenType.LeftBrace,
        ALKScriptTokenType.Identifier,
        ALKScriptTokenType.As,
        ALKScriptTokenType.Identifier,
        ALKScriptTokenType.RightBrace,
        ALKScriptTokenType.From,
        ALKScriptTokenType.String,
        ALKScriptTokenType.Semicolon,
        ALKScriptTokenType.EndOfFile
      },
      tokens.ConvertAll(t => t.Type));
  }

  [Fact]
  public void Tokenize_ExportDeclaration_ReturnsExpectedTokenSequence()
  {
    var lexer = new ALKScriptLexer();

    var tokens = lexer.Tokenize("export class Person {\n}");

    Assert.Equal(
      new[]
      {
        ALKScriptTokenType.Export,
        ALKScriptTokenType.Class,
        ALKScriptTokenType.Identifier,
        ALKScriptTokenType.LeftBrace,
        ALKScriptTokenType.RightBrace,
        ALKScriptTokenType.EndOfFile
      },
      tokens.ConvertAll(t => t.Type));
  }
}
