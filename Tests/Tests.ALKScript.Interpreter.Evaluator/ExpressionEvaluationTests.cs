using ALKScript.Interpreter.Common.Evaluation.Values;

namespace Tests.ALKScript.Interpreter.Evaluator;

public class ExpressionEvaluationTests : EvaluatorTestBase
{
  [Theory]
  [InlineData("1 + 2", 3L)]
  [InlineData("10 - 4", 6L)]
  [InlineData("3 * 4", 12L)]
  [InlineData("10 / 3", 3L)]
  [InlineData("10 % 3", 1L)]
  [InlineData("2 + 3 * 4", 14L)]
  [InlineData("(2 + 3) * 4", 20L)]
  public void Evaluate_IntegerArithmetic_ProducesIntValue(string expression, long expected)
  {
    var recorded = Run($"{RecordDeclaration} record({expression});");

    var value = Assert.IsType<IntValue>(Assert.Single(recorded));
    Assert.Equal(expected, value.Value);
  }

  [Fact]
  public void Evaluate_MixedIntAndFloatArithmetic_PromotesToFloat()
  {
    var recorded = Run($"{RecordDeclaration} record(1 + 2.5);");

    var value = Assert.IsType<FloatValue>(Assert.Single(recorded));
    Assert.Equal(3.5, value.Value);
  }

  [Fact]
  public void Evaluate_StringConcatenation_UsesPlusOperator()
  {
    var recorded = Run($"{RecordDeclaration} record(\"a\" + \"b\" + 1);");

    var value = Assert.IsType<StringValue>(Assert.Single(recorded));
    Assert.Equal("ab1", value.Value);
  }

  [Theory]
  [InlineData("1 < 2", true)]
  [InlineData("2 < 1", false)]
  [InlineData("2 <= 2", true)]
  [InlineData("3 > 2", true)]
  [InlineData("2 >= 3", false)]
  [InlineData("2 == 2", true)]
  [InlineData("2 != 2", false)]
  [InlineData("\"a\" == \"a\"", true)]
  [InlineData("\"a\" == \"b\"", false)]
  public void Evaluate_ComparisonAndEquality_ProducesBoolValue(string expression, bool expected)
  {
    var recorded = Run($"{RecordDeclaration} record({expression});");

    var value = Assert.IsType<BoolValue>(Assert.Single(recorded));
    Assert.Equal(expected, value.Value);
  }

  [Theory]
  [InlineData("!true", false)]
  [InlineData("!false", true)]
  [InlineData("!(1 == 1)", false)]
  public void Evaluate_LogicalNot_NegatesTruthiness(string expression, bool expected)
  {
    var recorded = Run($"{RecordDeclaration} record({expression});");

    var value = Assert.IsType<BoolValue>(Assert.Single(recorded));
    Assert.Equal(expected, value.Value);
  }

  [Fact]
  public void Evaluate_UnaryMinus_NegatesNumber()
  {
    var recorded = Run($"{RecordDeclaration} record(-5);");

    var value = Assert.IsType<IntValue>(Assert.Single(recorded));
    Assert.Equal(-5L, value.Value);
  }

  [Fact]
  public void Evaluate_LogicalAnd_ShortCircuitsWithoutEvaluatingRightOperand()
  {
    var recorded = Run($"{RecordDeclaration}\nfunction bool sideEffect() {{\n  record(\"called\");\n  return true;\n}}\nrecord(false && sideEffect());");

    var single = Assert.Single(recorded);
    var value = Assert.IsType<BoolValue>(single);
    Assert.False(value.Value);
  }

  [Fact]
  public void Evaluate_LogicalOr_ShortCircuitsWithoutEvaluatingRightOperand()
  {
    var recorded = Run($"{RecordDeclaration}\nfunction bool sideEffect() {{\n  record(\"called\");\n  return false;\n}}\nrecord(true || sideEffect());");

    var single = Assert.Single(recorded);
    var value = Assert.IsType<BoolValue>(single);
    Assert.True(value.Value);
  }

  [Fact]
  public void Evaluate_ArrayLiteralAndIndexing_ProducesArrayValueAndElements()
  {
    var recorded = Run($"{RecordDeclaration}\nvar items = [1, 2, 3];\nrecord(items);\nrecord(items[1]);");

    Assert.Equal(2, recorded.Count);

    var array = Assert.IsType<ArrayValue>(recorded[0]);
    Assert.Equal(3, array.Items.Count);

    var element = Assert.IsType<IntValue>(recorded[1]);
    Assert.Equal(2L, element.Value);
  }

  [Fact]
  public void Evaluate_IndexAssignment_MutatesArrayInPlace()
  {
    var recorded = Run($"{RecordDeclaration}\nvar items = [1, 2, 3];\nitems[0] = 9;\nrecord(items[0]);");

    var element = Assert.IsType<IntValue>(Assert.Single(recorded));
    Assert.Equal(9L, element.Value);
  }

  [Fact]
  public void Evaluate_NullLiteral_ProducesNullValue()
  {
    var recorded = Run($"{RecordDeclaration} record(null);");

    Assert.IsType<NullValue>(Assert.Single(recorded));
  }
}
