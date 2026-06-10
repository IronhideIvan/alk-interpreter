using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Parser;
using Xunit;

namespace Tests.ALKScript.Interpreter.Parser;

public class ClassMemberTests : ParserTestBase
{
  [Fact]
  public void Parse_ReadonlyField_ProducesFieldDeclWithIsReadonlyTrue()
  {
    var program = Parse("class Box {\n  public readonly int value;\n  public new(int value) { this.value = value; }\n}");

    var classDecl = Assert.IsType<ClassDecl>(Assert.Single(program.Declarations));
    var field = Assert.IsType<FieldDecl>(classDecl.Members[0]);

    Assert.Equal("value", field.Name.Lexeme);
    Assert.True(field.IsReadonly);
    Assert.False(field.IsStatic);
  }

  [Fact]
  public void Parse_NonReadonlyField_ProducesFieldDeclWithIsReadonlyFalse()
  {
    var program = Parse("class Box {\n  public int value;\n}");

    var classDecl = Assert.IsType<ClassDecl>(Assert.Single(program.Declarations));
    var field = Assert.IsType<FieldDecl>(classDecl.Members[0]);

    Assert.False(field.IsReadonly);
  }

  [Fact]
  public void Parse_StaticReadonlyField_ThrowsParseException()
  {
    Assert.Throws<ParseException>(() => Parse("class Box {\n  public static readonly int value;\n}"));
  }

  [Fact]
  public void Parse_ReadonlyOnMethod_ThrowsParseException()
  {
    Assert.Throws<ParseException>(() => Parse("class Box {\n  public readonly function void doThing() {}\n}"));
  }
}
