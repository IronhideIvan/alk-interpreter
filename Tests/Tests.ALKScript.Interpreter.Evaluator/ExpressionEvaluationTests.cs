using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Evaluator;

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

  // ── Bitwise operators ────────────────────────────────────────────────────────

  [Theory]
  [InlineData("6 & 3", 2L)]
  [InlineData("6 | 3", 7L)]
  [InlineData("6 ^ 3", 5L)]
  [InlineData("~0", -1L)]
  [InlineData("~5", -6L)]
  [InlineData("1 << 4", 16L)]
  [InlineData("256 >> 4", 16L)]
  [InlineData("1 | 2 & 3", 3L)]
  public void Evaluate_BitwiseExpression_ProducesIntValue(string expression, long expected)
  {
    var recorded = Run($"{RecordDeclaration} record({expression});");

    var value = Assert.IsType<IntValue>(Assert.Single(recorded));
    Assert.Equal(expected, value.Value);
  }

  [Theory]
  [InlineData("flags &= 3;", 2L)]
  [InlineData("flags |= 8;", 14L)]
  [InlineData("flags ^= 3;", 5L)]
  [InlineData("flags <<= 2;", 24L)]
  [InlineData("flags >>= 1;", 3L)]
  public void Evaluate_CompoundBitwiseAssignment_UpdatesVariable(string statement, long expected)
  {
    var recorded = Run($"{RecordDeclaration} var flags = 6; {statement} record(flags);");

    var value = Assert.IsType<IntValue>(Assert.Single(recorded));
    Assert.Equal(expected, value.Value);
  }

  [Fact]
  public void Evaluate_BitwiseOperatorOnFloat_ThrowsRuntimeException()
  {
    Assert.Throws<RuntimeException>(() =>
      Run($"{RecordDeclaration} record(1.5 & 2);"));
  }

  // ── String interpolation ─────────────────────────────────────────────────────

  [Fact]
  public void Evaluate_TemplateStringWithoutInterpolation_ProducesPlainString()
  {
    var recorded = Run($"{RecordDeclaration} record(`hello world`);");

    var value = Assert.IsType<StringValue>(Assert.Single(recorded));
    Assert.Equal("hello world", value.Value);
  }

  [Fact]
  public void Evaluate_TemplateStringWithInterpolation_SubstitutesExpressionValues()
  {
    var recorded = Run($"{RecordDeclaration} var name = \"Ada\"; record(`Hello, ${{name}}!`);");

    var value = Assert.IsType<StringValue>(Assert.Single(recorded));
    Assert.Equal("Hello, Ada!", value.Value);
  }

  [Fact]
  public void Evaluate_TemplateStringWithMultipleInterpolationsAndExpressions_ProducesExpectedString()
  {
    var recorded = Run($"{RecordDeclaration} var a = 2; var b = 3; record(`${{a}} + ${{b}} = ${{a + b}}`);");

    var value = Assert.IsType<StringValue>(Assert.Single(recorded));
    Assert.Equal("2 + 3 = 5", value.Value);
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

  // ── Array methods ──────────────────────────────────────────────────────────

  [Fact]
  public void Evaluate_ArrayLength_ReturnsItemCount()
  {
    var recorded = Run($"{RecordDeclaration}\nvar items = [1, 2, 3];\nrecord(items.length);");

    Assert.Equal(3L, Assert.IsType<IntValue>(Assert.Single(recorded)).Value);
  }

  [Fact]
  public void Evaluate_ArrayPush_AppendsElementAndReturnsNewLength()
  {
    var recorded = Run(
      $"{RecordDeclaration}\n" +
      "var items = [1, 2];\n" +
      "record(items.push(3));\n" +
      "record(items.length);\n" +
      "record(items[2]);");

    Assert.Equal(3, recorded.Count);
    Assert.Equal(3L, Assert.IsType<IntValue>(recorded[0]).Value); // new length
    Assert.Equal(3L, Assert.IsType<IntValue>(recorded[1]).Value); // items.length
    Assert.Equal(3L, Assert.IsType<IntValue>(recorded[2]).Value); // items[2]
  }

  [Fact]
  public void Evaluate_ArrayPop_RemovesAndReturnsLastElement()
  {
    var recorded = Run(
      $"{RecordDeclaration}\n" +
      "var items = [1, 2, 3];\n" +
      "record(items.pop());\n" +
      "record(items.length);");

    Assert.Equal(2, recorded.Count);
    Assert.Equal(3L, Assert.IsType<IntValue>(recorded[0]).Value); // popped value
    Assert.Equal(2L, Assert.IsType<IntValue>(recorded[1]).Value); // remaining length
  }

  [Fact]
  public void Evaluate_ArrayPop_OnEmptyArray_ThrowsRuntimeException()
  {
    var exception = Assert.Throws<RuntimeException>(() =>
      Run($"{RecordDeclaration}\nvar items = [];\nitems.pop();"));

    Assert.Contains("empty array", exception.Message);
  }

  [Fact]
  public void Evaluate_ArrayJoin_ReturnsNewArrayWithCombinedElements()
  {
    var recorded = Run(
      $"{RecordDeclaration}\n" +
      "var a = [1, 2];\n" +
      "var b = [3, 4];\n" +
      "var c = a.join(b);\n" +
      "record(c);\n" +
      "record(a.length);\n" + // 'a' is unchanged
      "record(c.length);");

    Assert.Equal(3, recorded.Count);

    var combined = Assert.IsType<ArrayValue>(recorded[0]);
    Assert.Equal(new long[] { 1, 2, 3, 4 }, combined.Items.ConvertAll(item => ((IntValue)item).Value));

    Assert.Equal(2L, Assert.IsType<IntValue>(recorded[1]).Value);
    Assert.Equal(4L, Assert.IsType<IntValue>(recorded[2]).Value);
  }

  [Fact]
  public void Evaluate_ArraySlice_ReturnsNewArrayWithRange()
  {
    var recorded = Run(
      $"{RecordDeclaration}\n" +
      "var items = [10, 20, 30, 40, 50];\n" +
      "var middle = items.slice(1, 3);\n" +
      "record(middle);\n" +
      "record(items.length);"); // 'items' is unchanged

    Assert.Equal(2, recorded.Count);

    var slice = Assert.IsType<ArrayValue>(recorded[0]);
    Assert.Equal(new long[] { 20, 30, 40 }, slice.Items.ConvertAll(item => ((IntValue)item).Value));

    Assert.Equal(5L, Assert.IsType<IntValue>(recorded[1]).Value);
  }

  [Fact]
  public void Evaluate_ArraySlice_OutOfRange_ThrowsRuntimeException()
  {
    var exception = Assert.Throws<RuntimeException>(() =>
      Run($"{RecordDeclaration}\nvar items = [1, 2, 3];\nitems.slice(2, 5);"));

    Assert.Contains("out of bounds", exception.Message);
  }

  [Fact]
  public void Evaluate_ArrayRemove_RemovesElementInPlaceAndReturnsIt()
  {
    var recorded = Run(
      $"{RecordDeclaration}\n" +
      "var items = [1, 2, 3];\n" +
      "record(items.remove(1));\n" +
      "record(items.length);\n" +
      "record(items);");

    Assert.Equal(3, recorded.Count);

    Assert.Equal(2L, Assert.IsType<IntValue>(recorded[0]).Value); // removed value
    Assert.Equal(2L, Assert.IsType<IntValue>(recorded[1]).Value); // new length

    var remaining = Assert.IsType<ArrayValue>(recorded[2]);
    Assert.Equal(new long[] { 1, 3 }, remaining.Items.ConvertAll(item => ((IntValue)item).Value));
  }

  [Fact]
  public void Evaluate_ArrayRemove_OutOfRange_ThrowsRuntimeException()
  {
    var exception = Assert.Throws<RuntimeException>(() =>
      Run($"{RecordDeclaration}\nvar items = [1, 2, 3];\nitems.remove(5);"));

    Assert.Contains("out of bounds", exception.Message);
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

  // ── Compound assignment ───────────────────────────────────────────────────

  [Theory]
  [InlineData("var x = 10; x += 3;",  13L)]
  [InlineData("var x = 10; x -= 3;",   7L)]
  [InlineData("var x = 10; x *= 3;",  30L)]
  [InlineData("var x = 10; x /= 2;",   5L)]
  [InlineData("var x = 10; x %= 3;",   1L)]
  public void Evaluate_CompoundAssignment_UpdatesVariableCorrectly(string source, long expected)
  {
    var recorded = Run($"{RecordDeclaration} {source} record(x);");

    var value = Assert.IsType<IntValue>(Assert.Single(recorded));
    Assert.Equal(expected, value.Value);
  }

  [Fact]
  public void Evaluate_CompoundAssignment_StringConcatenation_WorksWithPlusEqual()
  {
    var recorded = Run($"{RecordDeclaration} var s = \"hello\"; s += \" world\"; record(s);");

    var value = Assert.IsType<StringValue>(Assert.Single(recorded));
    Assert.Equal("hello world", value.Value);
  }

  [Fact]
  public void Evaluate_CompoundAssignment_OnArrayElement_MutatesInPlace()
  {
    var recorded = Run($"{RecordDeclaration} var arr = [1, 2, 3]; arr[1] += 10; record(arr[1]);");

    var value = Assert.IsType<IntValue>(Assert.Single(recorded));
    Assert.Equal(12L, value.Value);
  }

  // ── Ternary operator ─────────────────────────────────────────────────────

  [Fact]
  public void Evaluate_TernaryOperator_ReturnsThenBranchWhenConditionTruthy()
  {
    var recorded = Run($"{RecordDeclaration} record(true ? \"yes\" : \"no\");");

    var value = Assert.IsType<StringValue>(Assert.Single(recorded));
    Assert.Equal("yes", value.Value);
  }

  [Fact]
  public void Evaluate_TernaryOperator_ReturnsElseBranchWhenConditionFalsy()
  {
    var recorded = Run($"{RecordDeclaration} record(false ? \"yes\" : \"no\");");

    var value = Assert.IsType<StringValue>(Assert.Single(recorded));
    Assert.Equal("no", value.Value);
  }

  [Fact]
  public void Evaluate_TernaryOperator_EvaluatesOnlyChosenBranch()
  {
    // Side-effects in the un-taken branch must not run.
    var recorded = Run($"{RecordDeclaration}\nfunction string side() {{\n  record(\"side\");\n  return \"side\";\n}}\nrecord(true ? \"a\" : side());");

    // Only "a" should be recorded — "side" must not appear.
    var single = Assert.Single(recorded);
    Assert.Equal("a", Assert.IsType<StringValue>(single).Value);
  }

  // ── Null coalescing (??) ─────────────────────────────────────────────────

  [Fact]
  public void Evaluate_NullCoalescing_ReturnsLeftOperandWhenNonNull()
  {
    var recorded = Run($"{RecordDeclaration} var x = 42; record(x ?? 99);");

    Assert.Equal(42L, Assert.IsType<IntValue>(Assert.Single(recorded)).Value);
  }

  [Fact]
  public void Evaluate_NullCoalescing_ReturnsFallbackWhenLeftIsNull()
  {
    var recorded = Run($"{RecordDeclaration} var x = null; record(x ?? 99);");

    Assert.Equal(99L, Assert.IsType<IntValue>(Assert.Single(recorded)).Value);
  }

  [Fact]
  public void Evaluate_NullCoalescing_DoesNotEvaluateRightSideWhenLeftIsNonNull()
  {
    // The right-hand side should never be evaluated when the left is non-null.
    var recorded = Run($"{RecordDeclaration}\nfunction int side() {{\n  record(\"side\");\n  return 0;\n}}\nvar x = 1;\nrecord(x ?? side());");

    // Only the "1" result should appear — "side" must not run.
    var single = Assert.Single(recorded);
    Assert.Equal(1L, Assert.IsType<IntValue>(single).Value);
  }

  // ── Null-conditional (?.) ────────────────────────────────────────────────

  [Fact]
  public void Evaluate_NullConditionalGet_ReturnsNullWhenTargetIsNull()
  {
    var recorded = Run($"{RecordDeclaration} var x = null; record(x?.name);");

    Assert.IsType<NullValue>(Assert.Single(recorded));
  }

  [Fact]
  public void Evaluate_NullConditionalGet_ReturnsMemberValueWhenTargetIsNonNull()
  {
    var recorded = Run(
      $"{RecordDeclaration}\n" +
      "class Box { public string value; public new(string v) { this.value = v; } }\n" +
      "var b = new Box(\"hi\");\n" +
      "record(b?.value);");

    Assert.Equal("hi", Assert.IsType<StringValue>(Assert.Single(recorded)).Value);
  }

  [Fact]
  public void Evaluate_NullConditionalCall_ReturnsNullWhenTargetIsNull()
  {
    var recorded = Run(
      $"{RecordDeclaration}\n" +
      "class Greeter { public function string greet() { return \"hello\"; } }\n" +
      "var g = null;\n" +
      "record(g?.greet());");

    Assert.IsType<NullValue>(Assert.Single(recorded));
  }
}
