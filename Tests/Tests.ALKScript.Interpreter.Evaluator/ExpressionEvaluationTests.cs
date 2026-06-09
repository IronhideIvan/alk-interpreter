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

  // ── Prefix / postfix increment and decrement ──────────────────────────────

  [Fact]
  public void Evaluate_PostfixIncrement_ReturnsOldValueAndMutatesVariable()
  {
    // "x++" must yield the value *before* the increment.
    var recorded = Run($"{RecordDeclaration} var x = 5; record(x++); record(x);");

    Assert.Equal(2, recorded.Count);
    Assert.Equal(5L, Assert.IsType<IntValue>(recorded[0]).Value); // expression result
    Assert.Equal(6L, Assert.IsType<IntValue>(recorded[1]).Value); // updated variable
  }

  [Fact]
  public void Evaluate_PostfixDecrement_ReturnsOldValueAndMutatesVariable()
  {
    var recorded = Run($"{RecordDeclaration} var x = 5; record(x--); record(x);");

    Assert.Equal(2, recorded.Count);
    Assert.Equal(5L, Assert.IsType<IntValue>(recorded[0]).Value);
    Assert.Equal(4L, Assert.IsType<IntValue>(recorded[1]).Value);
  }

  [Fact]
  public void Evaluate_PrefixIncrement_ReturnsNewValueAndMutatesVariable()
  {
    // "++x" must yield the value *after* the increment.
    var recorded = Run($"{RecordDeclaration} var x = 5; record(++x); record(x);");

    Assert.Equal(2, recorded.Count);
    Assert.Equal(6L, Assert.IsType<IntValue>(recorded[0]).Value); // expression result
    Assert.Equal(6L, Assert.IsType<IntValue>(recorded[1]).Value); // updated variable
  }

  [Fact]
  public void Evaluate_PrefixDecrement_ReturnsNewValueAndMutatesVariable()
  {
    var recorded = Run($"{RecordDeclaration} var x = 5; record(--x); record(x);");

    Assert.Equal(2, recorded.Count);
    Assert.Equal(4L, Assert.IsType<IntValue>(recorded[0]).Value);
    Assert.Equal(4L, Assert.IsType<IntValue>(recorded[1]).Value);
  }

  [Fact]
  public void Evaluate_PostfixIncrement_OnFloatVariable_StepsByOne()
  {
    var recorded = Run($"{RecordDeclaration} var x = 1.5; record(x++); record(x);");

    Assert.Equal(2, recorded.Count);
    Assert.Equal(1.5, Assert.IsType<FloatValue>(recorded[0]).Value);
    Assert.Equal(2.5, Assert.IsType<FloatValue>(recorded[1]).Value);
  }

  [Fact]
  public void Evaluate_PrefixIncrement_OnArrayElement_MutatesElementInPlace()
  {
    // "++arr[1]" updates the element at index 1 and returns the new value.
    var recorded = Run($"{RecordDeclaration} var arr = [10, 20, 30]; record(++arr[1]); record(arr[1]);");

    Assert.Equal(2, recorded.Count);
    Assert.Equal(21L, Assert.IsType<IntValue>(recorded[0]).Value);
    Assert.Equal(21L, Assert.IsType<IntValue>(recorded[1]).Value);
  }

  [Fact]
  public void Evaluate_PostfixDecrement_OnArrayElement_ReturnsOldValueAndMutatesInPlace()
  {
    var recorded = Run($"{RecordDeclaration} var arr = [10, 20, 30]; record(arr[2]--); record(arr[2]);");

    Assert.Equal(2, recorded.Count);
    Assert.Equal(30L, Assert.IsType<IntValue>(recorded[0]).Value);
    Assert.Equal(29L, Assert.IsType<IntValue>(recorded[1]).Value);
  }

  [Fact]
  public void Evaluate_PostfixIncrement_InForLoopCondition_WorksAsExpected()
  {
    // Common idiom: "i++" as the for-loop increment expression.
    // Equivalent to the "i = i + 1" form used in the existing end-to-end tests.
    var recorded = Run(
      $"{RecordDeclaration} for (var i = 0; i < 3; i++) {{ record(i); }}");

    Assert.Equal(3, recorded.Count);
    Assert.Equal(0L, Assert.IsType<IntValue>(recorded[0]).Value);
    Assert.Equal(1L, Assert.IsType<IntValue>(recorded[1]).Value);
    Assert.Equal(2L, Assert.IsType<IntValue>(recorded[2]).Value);
  }

  [Fact]
  public void Evaluate_IncrementOnNonNumericValue_ThrowsRuntimeException()
  {
    Assert.ThrowsAny<Exception>(() => Run("var s = \"hello\"; s++;"));
  }
}
