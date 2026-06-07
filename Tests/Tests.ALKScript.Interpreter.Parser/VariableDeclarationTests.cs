using ALKScript.Interpreter.Parser.Ast;

namespace Tests.ALKScript.Interpreter.Parser;

public class VariableDeclarationTests : ParserTestBase
{
  [Fact]
  public void Parse_InferredDeclaration_ProducesVariableDeclWithNullType()
  {
    var program = Parse("var num = 1;");

    var decl = Assert.IsType<VariableDecl>(Assert.Single(program.Declarations));
    Assert.Null(decl.Type);
    Assert.Equal("num", decl.Name.Lexeme);

    var literal = Assert.IsType<LiteralExpr>(decl.Initializer);
    Assert.Equal(1, literal.Value);
  }

  [Fact]
  public void Parse_ExplicitlyTypedDeclaration_ProducesVariableDeclWithType()
  {
    var program = Parse("int num = 1;");

    var decl = Assert.IsType<VariableDecl>(Assert.Single(program.Declarations));
    Assert.NotNull(decl.Type);
    Assert.Equal("int", decl.Type!.Name);
    Assert.Equal("num", decl.Name.Lexeme);
  }

  [Fact]
  public void Parse_ExplicitlyTypedDeclarationWithoutInitializer_ProducesVariableDeclWithNullInitializer()
  {
    var program = Parse("string name;");

    var decl = Assert.IsType<VariableDecl>(Assert.Single(program.Declarations));
    Assert.NotNull(decl.Type);
    Assert.Equal("string", decl.Type!.Name);
    Assert.Null(decl.Initializer);
  }

  [Fact]
  public void Parse_ArrayDeclaration_ProducesTypeWithArrayRank()
  {
    var program = Parse("int[] numArr = [1, 2, 3, 4];");

    var decl = Assert.IsType<VariableDecl>(Assert.Single(program.Declarations));
    Assert.NotNull(decl.Type);
    Assert.Equal("int", decl.Type!.Name);
    Assert.Equal(1, decl.Type.ArrayRank);

    var array = Assert.IsType<ArrayLiteralExpr>(decl.Initializer);
    Assert.Equal(4, array.Elements.Count);
  }

  [Fact]
  public void Parse_NullableTypeDeclaration_ProducesNullableType()
  {
    var program = Parse("string? name = null;");

    var decl = Assert.IsType<VariableDecl>(Assert.Single(program.Declarations));
    Assert.NotNull(decl.Type);
    Assert.Equal("string", decl.Type!.Name);
    Assert.True(decl.Type.IsNullable);

    var literal = Assert.IsType<LiteralExpr>(decl.Initializer);
    Assert.Null(literal.Value);
  }

  [Fact]
  public void Parse_LongLiteralInitializer_ProducesLongValue()
  {
    var program = Parse("long big = 42L;");

    var decl = Assert.IsType<VariableDecl>(Assert.Single(program.Declarations));
    var literal = Assert.IsType<LiteralExpr>(decl.Initializer);
    Assert.Equal(42L, literal.Value);
  }

  [Fact]
  public void Parse_GenericTypeDeclaration_ProducesTypeArguments()
  {
    var program = Parse("Array<int> list = new Array<int>();");

    var decl = Assert.IsType<VariableDecl>(Assert.Single(program.Declarations));
    Assert.NotNull(decl.Type);
    Assert.Equal("Array", decl.Type!.Name);
    var typeArgument = Assert.Single(decl.Type.TypeArguments);
    Assert.Equal("int", typeArgument.Name);

    var newExpr = Assert.IsType<NewExpr>(decl.Initializer);
    Assert.Equal("Array", newExpr.TypeName.Lexeme);
    Assert.Single(newExpr.TypeArguments);
  }
}
