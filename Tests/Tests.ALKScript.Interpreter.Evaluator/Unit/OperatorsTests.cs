using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Common.Token;
using ALKScript.Interpreter.Evaluator;

namespace Tests.ALKScript.Interpreter.Evaluator.Unit;

public class OperatorsTests
{
  private static readonly ALKScriptToken Op = Nodes.Operator(ALKScriptTokenType.Plus, "+");

  [Fact]
  public void Add_BothInts_ProducesIntSum()
  {
    var result = Operators.Add(new IntValue(1), new IntValue(2), Op);

    Assert.Equal(3L, Assert.IsType<IntValue>(result).Value);
  }

  [Fact]
  public void Add_EitherOperandIsString_ConcatenatesStringRepresentations()
  {
    var result = Operators.Add(new StringValue("a"), new IntValue(1), Op);

    Assert.Equal("a1", Assert.IsType<StringValue>(result).Value);
  }

  [Fact]
  public void Arithmetic_BothInts_UsesIntOperation()
  {
    var result = Operators.Arithmetic(new IntValue(6), new IntValue(2), Op, (a, b) => a / b, (a, b) => a / b);

    Assert.Equal(3L, Assert.IsType<IntValue>(result).Value);
  }

  [Fact]
  public void Arithmetic_MixedIntAndFloat_PromotesToFloatOperation()
  {
    var result = Operators.Arithmetic(new IntValue(1), new FloatValue(2.5), Op, (a, b) => a + b, (a, b) => a + b);

    Assert.Equal(3.5, Assert.IsType<FloatValue>(result).Value);
  }

  [Fact]
  public void Arithmetic_NonNumericOperand_ThrowsRuntimeException()
  {
    var exception = Assert.Throws<RuntimeException>(() => Operators.Arithmetic(new StringValue("a"), new IntValue(1), Op, (a, b) => a + b, (a, b) => a + b));

    Assert.Contains("cannot be applied to 'string' and 'int'", exception.Message);
  }

  [Fact]
  public void Compare_Strings_ComparesOrdinally()
  {
    Assert.True(Operators.Compare(new StringValue("a"), new StringValue("b"), Op) < 0);
  }

  [Fact]
  public void Compare_Numbers_ComparesNumerically()
  {
    Assert.True(Operators.Compare(new IntValue(1), new FloatValue(1.5), Op) < 0);
  }

  [Fact]
  public void Compare_NonComparableOperands_ThrowsRuntimeException()
  {
    Assert.Throws<RuntimeException>(() => Operators.Compare(BoolValue.True, new IntValue(1), Op));
  }

  [Theory]
  [InlineData(1L, 1L, true)]
  [InlineData(1L, 2L, false)]
  public void AreEqual_Ints_ComparesByValue(long left, long right, bool expected)
  {
    Assert.Equal(expected, Operators.AreEqual(new IntValue(left), new IntValue(right)));
  }

  [Fact]
  public void AreEqual_NumericTypesCompareAcrossIntAndFloat()
  {
    Assert.True(Operators.AreEqual(new IntValue(1), new FloatValue(1.0)));
  }

  [Fact]
  public void AreEqual_NullValues_AreEqualToEachOtherOnly()
  {
    Assert.True(Operators.AreEqual(NullValue.Instance, NullValue.Instance));
    Assert.False(Operators.AreEqual(NullValue.Instance, new IntValue(0)));
  }

  [Fact]
  public void AreEqual_MismatchedNonNumericTypes_AreNotEqual()
  {
    Assert.False(Operators.AreEqual(new StringValue("1"), new IntValue(1)));
  }

  [Fact]
  public void TryToNumber_IntAndFloat_Succeed()
  {
    Assert.True(Operators.TryToNumber(new IntValue(2), out var fromInt));
    Assert.Equal(2.0, fromInt);

    Assert.True(Operators.TryToNumber(new FloatValue(2.5), out var fromFloat));
    Assert.Equal(2.5, fromFloat);
  }

  [Fact]
  public void TryToNumber_NonNumericValue_Fails()
  {
    Assert.False(Operators.TryToNumber(new StringValue("2"), out var number));
    Assert.Equal(0, number);
  }

  [Fact]
  public void Stringify_UsesValueToString()
  {
    Assert.Equal("42", Operators.Stringify(new IntValue(42)));
    Assert.Equal("null", Operators.Stringify(NullValue.Instance));
  }
}
