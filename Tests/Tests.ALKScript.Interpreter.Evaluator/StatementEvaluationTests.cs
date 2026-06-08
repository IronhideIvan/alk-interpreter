using ALKScript.Interpreter.Common.Evaluation.Values;

namespace Tests.ALKScript.Interpreter.Evaluator;

public class StatementEvaluationTests : EvaluatorTestBase
{
  [Fact]
  public void Evaluate_VariableDeclarationWithInitializer_BindsValue()
  {
    var recorded = Run($"{RecordDeclaration}\nvar x = 1 + 2;\nrecord(x);");

    var value = Assert.IsType<IntValue>(Assert.Single(recorded));
    Assert.Equal(3L, value.Value);
  }

  [Fact]
  public void Evaluate_VariableDeclarationWithoutInitializer_DefaultsToNull()
  {
    var recorded = Run($"{RecordDeclaration}\nint x;\nrecord(x);");

    Assert.IsType<NullValue>(Assert.Single(recorded));
  }

  [Fact]
  public void Evaluate_Assignment_UpdatesExistingBinding()
  {
    var recorded = Run($"{RecordDeclaration}\nvar x = 1;\nx = x + 1;\nrecord(x);");

    var value = Assert.IsType<IntValue>(Assert.Single(recorded));
    Assert.Equal(2L, value.Value);
  }

  [Fact]
  public void Evaluate_IfStatement_ExecutesThenBranchWhenConditionIsTruthy()
  {
    var recorded = Run($"{RecordDeclaration}\nif (1 < 2) {{\n  record(\"then\");\n}} else {{\n  record(\"else\");\n}}");

    var value = Assert.IsType<StringValue>(Assert.Single(recorded));
    Assert.Equal("then", value.Value);
  }

  [Fact]
  public void Evaluate_IfStatement_ExecutesElseBranchWhenConditionIsFalsy()
  {
    var recorded = Run($"{RecordDeclaration}\nif (2 < 1) {{\n  record(\"then\");\n}} else {{\n  record(\"else\");\n}}");

    var value = Assert.IsType<StringValue>(Assert.Single(recorded));
    Assert.Equal("else", value.Value);
  }

  [Fact]
  public void Evaluate_WhileStatement_LoopsUntilConditionIsFalse()
  {
    var recorded = Run($"{RecordDeclaration}\nvar i = 0;\nwhile (i < 3) {{\n  record(i);\n  i = i + 1;\n}}");

    Assert.Equal(3, recorded.Count);
    Assert.Equal(new long[] { 0, 1, 2 }, recorded.Select(v => Assert.IsType<IntValue>(v).Value));
  }

  [Fact]
  public void Evaluate_ForStatement_RunsInitializerConditionAndIncrement()
  {
    var recorded = Run($"{RecordDeclaration}\nfor (var i = 0; i < 3; i = i + 1) {{\n  record(i);\n}}");

    Assert.Equal(3, recorded.Count);
    Assert.Equal(new long[] { 0, 1, 2 }, recorded.Select(v => Assert.IsType<IntValue>(v).Value));
  }

  [Fact]
  public void Evaluate_ForStatementWithOmittedInitializerAndIncrement_StillEvaluatesCondition()
  {
    var recorded = Run($"{RecordDeclaration}\nvar i = 0;\nfor (; i < 2;) {{\n  record(i);\n  i = i + 1;\n}}");

    Assert.Equal(2, recorded.Count);
    Assert.Equal(new long[] { 0, 1 }, recorded.Select(v => Assert.IsType<IntValue>(v).Value));
  }

  [Fact]
  public void Evaluate_BlockStatement_IntroducesNewScopeThatShadowsOuterBinding()
  {
    var recorded = Run($"{RecordDeclaration}\nvar x = 1;\n{{\n  var x = 2;\n  record(x);\n}}\nrecord(x);");

    Assert.Equal(2, recorded.Count);
    Assert.Equal(2L, Assert.IsType<IntValue>(recorded[0]).Value);
    Assert.Equal(1L, Assert.IsType<IntValue>(recorded[1]).Value);
  }

  [Fact]
  public void Evaluate_TryCatch_RunsCatchClauseWithThrownValueBound()
  {
    var recorded = Run($"{RecordDeclaration}\ntry {{\n  throw \"boom\";\n}} catch (string message) {{\n  record(message);\n}}");

    var value = Assert.IsType<StringValue>(Assert.Single(recorded));
    Assert.Equal("boom", value.Value);
  }

  [Fact]
  public void Evaluate_TryFinally_RunsFinallyBlockEvenWhenNoExceptionIsThrown()
  {
    var recorded = Run($"{RecordDeclaration}\ntry {{\n  record(\"try\");\n}} finally {{\n  record(\"finally\");\n}}");

    Assert.Equal(new[] { "try", "finally" }, recorded.Select(v => Assert.IsType<StringValue>(v).Value));
  }
}
