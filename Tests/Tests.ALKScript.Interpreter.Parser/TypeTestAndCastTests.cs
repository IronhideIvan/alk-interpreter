using ALKScript.Interpreter.Common.Ast;
using Xunit;

namespace Tests.ALKScript.Interpreter.Parser;

public class TypeTestAndCastTests : ParserTestBase
{
  [Fact]
  public void Parse_IsExpression_ProducesTypeTestExpr()
  {
    var program = Parse("var ok = x is Animal;");

    var declaration = Assert.IsType<VariableDecl>(Assert.Single(program.Declarations));
    var typeTest = Assert.IsType<TypeTestExpr>(declaration.Initializer);

    Assert.IsType<IdentifierExpr>(typeTest.Operand);
    Assert.Equal("Animal", typeTest.Type.Name);
  }

  [Fact]
  public void Parse_AsExpression_ProducesTypeCastExpr()
  {
    var program = Parse("var dog = x as Dog;");

    var declaration = Assert.IsType<VariableDecl>(Assert.Single(program.Declarations));
    var typeCast = Assert.IsType<TypeCastExpr>(declaration.Initializer);

    Assert.IsType<IdentifierExpr>(typeCast.Operand);
    Assert.Equal("Dog", typeCast.Type.Name);
  }

  [Fact]
  public void Parse_IsExpressionWithNullableType_CapturesNullableFlag()
  {
    var program = Parse("var ok = x is string?;");

    var declaration = Assert.IsType<VariableDecl>(Assert.Single(program.Declarations));
    var typeTest = Assert.IsType<TypeTestExpr>(declaration.Initializer);

    Assert.Equal("string", typeTest.Type.Name);
    Assert.True(typeTest.Type.IsNullable);
  }

  [Fact]
  public void Parse_ChainedIsAndComparison_BindsTighterThanRelational()
  {
    // "x is int" should bind as a unit before "==" applies.
    var program = Parse("var ok = (x is int) == true;");

    var declaration = Assert.IsType<VariableDecl>(Assert.Single(program.Declarations));
    var binary = Assert.IsType<BinaryExpr>(declaration.Initializer);
    var grouping = Assert.IsType<GroupingExpr>(binary.Left);
    Assert.IsType<TypeTestExpr>(grouping.Expression);
  }

  [Theory]
  [InlineData("(int)b", "int")]
  [InlineData("(long)b", "long")]
  [InlineData("(float)b", "float")]
  public void Parse_NumericCast_ProducesCastExpr(string expression, string targetType)
  {
    var program = Parse($"var a = {expression};");

    var declaration = Assert.IsType<VariableDecl>(Assert.Single(program.Declarations));
    var cast = Assert.IsType<CastExpr>(declaration.Initializer);

    Assert.Equal(targetType, cast.TargetType);
    Assert.IsType<IdentifierExpr>(cast.Operand);
  }

  [Fact]
  public void Parse_ParenthesizedIdentifier_RemainsGroupingExpr()
  {
    // "(b)" is not a numeric cast — "b" is not a primitive type keyword —
    // so it must still parse as a plain grouping expression.
    var program = Parse("var a = (b);");

    var declaration = Assert.IsType<VariableDecl>(Assert.Single(program.Declarations));
    var grouping = Assert.IsType<GroupingExpr>(declaration.Initializer);
    Assert.IsType<IdentifierExpr>(grouping.Expression);
  }
}
