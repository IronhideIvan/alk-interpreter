using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Evaluator;
using ALKScript.Interpreter.Parser;

namespace Tests.ALKScript.Interpreter.Evaluator;

public class OperatorOverloadTests : EvaluatorTestBase
{
  private const string Vector2Class = @"
class Vector2 {
    public float x;
    public float y;
    public new(float x, float y) {
        this.x = x;
        this.y = y;
    }
    public static operator Vector2 +(Vector2 a, Vector2 b) {
        return new Vector2(a.x + b.x, a.y + b.y);
    }
    public static operator Vector2 -(Vector2 a, Vector2 b) {
        return new Vector2(a.x - b.x, a.y - b.y);
    }
    public static operator Vector2 -(Vector2 a) {
        return new Vector2(-a.x, -a.y);
    }
    public static operator Vector2 *(Vector2 a, float s) {
        return new Vector2(a.x * s, a.y * s);
    }
    public static operator bool ==(Vector2 a, Vector2 b) {
        return a.x == b.x && a.y == b.y;
    }
}
";

  // ── Binary operators ────────────────────────────────────────────────────────

  [Fact]
  public void OperatorOverload_BinaryPlus_AddsTwoVectors()
  {
    var recorded = Run($"{RecordDeclaration}{Vector2Class}var v = new Vector2(1.0, 2.0) + new Vector2(3.0, 4.0);\nrecord(v.x);\nrecord(v.y);");
    Assert.Equal(4.0, ((FloatValue)recorded[0]).Value);
    Assert.Equal(6.0, ((FloatValue)recorded[1]).Value);
  }

  [Fact]
  public void OperatorOverload_BinaryMinus_SubtractsVectors()
  {
    var recorded = Run($"{RecordDeclaration}{Vector2Class}var v = new Vector2(5.0, 3.0) - new Vector2(2.0, 1.0);\nrecord(v.x);\nrecord(v.y);");
    Assert.Equal(3.0, ((FloatValue)recorded[0]).Value);
    Assert.Equal(2.0, ((FloatValue)recorded[1]).Value);
  }

  // ── Unary minus ─────────────────────────────────────────────────────────────

  [Fact]
  public void OperatorOverload_UnaryMinus_NegatesVector()
  {
    var recorded = Run($"{RecordDeclaration}{Vector2Class}var v = -new Vector2(3.0, -4.0);\nrecord(v.x);\nrecord(v.y);");
    Assert.Equal(-3.0, ((FloatValue)recorded[0]).Value);
    Assert.Equal(4.0, ((FloatValue)recorded[1]).Value);
  }

  // ── Equality with == overload ────────────────────────────────────────────────

  [Fact]
  public void OperatorOverload_EqualEqual_UsesOverload()
  {
    var recorded = Run($"{RecordDeclaration}{Vector2Class}var a = new Vector2(1.0, 2.0);\nvar b = new Vector2(1.0, 2.0);\nvar c = new Vector2(3.0, 4.0);\nrecord(a == b);\nrecord(a == c);");
    Assert.Equal(BoolValue.True, recorded[0]);
    Assert.Equal(BoolValue.False, recorded[1]);
  }

  [Fact]
  public void OperatorOverload_BangEqual_AutoNegatesFromEqualEqual()
  {
    var recorded = Run($"{RecordDeclaration}{Vector2Class}var a = new Vector2(1.0, 2.0);\nvar b = new Vector2(1.0, 2.0);\nvar c = new Vector2(3.0, 4.0);\nrecord(a != b);\nrecord(a != c);");
    Assert.Equal(BoolValue.False, recorded[0]);
    Assert.Equal(BoolValue.True, recorded[1]);
  }

  // ── Non-static operator is rejected at parse time ───────────────────────────

  [Fact]
  public void OperatorOverload_NonStatic_ThrowsParseError()
  {
    Assert.Throws<ParseException>(() =>
      Run($"{RecordDeclaration}class Foo {{\n    public operator Foo +(Foo a, Foo b) {{ return a; }}\n}}"));
  }

  // ── Only static is valid ─────────────────────────────────────────────────────

  [Fact]
  public void OperatorOverload_WrongArity_ThrowsParseError()
  {
    Assert.Throws<ParseException>(() =>
      Run($"{RecordDeclaration}class Foo {{\n    public static operator Foo +(Foo a, Foo b, Foo c) {{ return a; }}\n}}"));
  }

  // ── Unary vs binary disambiguation ──────────────────────────────────────────

  [Fact]
  public void OperatorOverload_UnaryAndBinaryMinusBothDefined_DisambiguatesByArity()
  {
    // Binary used in a-b, unary used in -a
    var recorded = Run($"{RecordDeclaration}{Vector2Class}" +
      "var a = new Vector2(5.0, 3.0);\n" +
      "var neg = -a;\n" +
      "var diff = a - new Vector2(1.0, 1.0);\n" +
      "record(neg.x);\nrecord(diff.x);");
    Assert.Equal(-5.0, ((FloatValue)recorded[0]).Value);
    Assert.Equal(4.0, ((FloatValue)recorded[1]).Value);
  }
}
