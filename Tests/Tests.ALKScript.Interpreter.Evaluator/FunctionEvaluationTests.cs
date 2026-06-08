using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Evaluator;

namespace Tests.ALKScript.Interpreter.Evaluator;

public class FunctionEvaluationTests : EvaluatorTestBase
{
  [Fact]
  public void Evaluate_FunctionCall_ReturnsValueFromReturnStatement()
  {
    var recorded = Run($"{RecordDeclaration}\nfunction int add(int a, int b) {{\n  return a + b;\n}}\nrecord(add(2, 3));");

    var value = Assert.IsType<IntValue>(Assert.Single(recorded));
    Assert.Equal(5L, value.Value);
  }

  [Fact]
  public void Evaluate_FunctionWithoutReturnStatement_ReturnsNull()
  {
    var recorded = Run($"{RecordDeclaration}\nfunction void noop() {{\n}}\nrecord(noop());");

    Assert.IsType<NullValue>(Assert.Single(recorded));
  }

  [Fact]
  public void Evaluate_RecursiveFunction_ComputesExpectedResult()
  {
    var recorded = Run($"{RecordDeclaration}\nfunction int factorial(int n) {{\n  if (n <= 1) {{\n    return 1;\n  }}\n  return n * factorial(n - 1);\n}}\nrecord(factorial(5));");

    var value = Assert.IsType<IntValue>(Assert.Single(recorded));
    Assert.Equal(120L, value.Value);
  }

  [Fact]
  public void Evaluate_NestedFunction_CapturesEnclosingScopeAsClosure()
  {
    var recorded = Run($"{RecordDeclaration}\nfunction int outer() {{\n  var captured = 10;\n  function int inner() {{\n    return captured + 1;\n  }}\n  return inner();\n}}\nrecord(outer());");

    var value = Assert.IsType<IntValue>(Assert.Single(recorded));
    Assert.Equal(11L, value.Value);
  }

  [Fact]
  public void Evaluate_FunctionCalledWithWrongArgumentCount_ThrowsRuntimeException()
  {
    var exception = Assert.Throws<RuntimeException>(() =>
      Run($"{RecordDeclaration}\nfunction int add(int a, int b) {{\n  return a + b;\n}}\nadd(1);"));

    Assert.Contains("Expected 2 argument", exception.Message);
  }

  [Fact]
  public void Evaluate_CallingNonCallableValue_ThrowsRuntimeException()
  {
    var exception = Assert.Throws<RuntimeException>(() =>
      Run($"{RecordDeclaration}\nvar x = 1;\nx();"));

    Assert.Contains("Cannot call a value of type 'int'", exception.Message);
  }

  [Fact]
  public void Evaluate_ReferenceToUndefinedName_ThrowsRuntimeException()
  {
    var exception = Assert.Throws<RuntimeException>(() => Run($"{RecordDeclaration}\nrecord(doesNotExist);"));

    Assert.Contains("Undefined name 'doesNotExist'", exception.Message);
  }
}
