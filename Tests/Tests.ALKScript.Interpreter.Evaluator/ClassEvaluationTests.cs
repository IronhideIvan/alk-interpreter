using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Evaluator;

namespace Tests.ALKScript.Interpreter.Evaluator;

public class ClassEvaluationTests : EvaluatorTestBase
{
  [Fact]
  public void Evaluate_NewExpression_ConstructsInstanceAndRunsConstructor()
  {
    var recorded = Run($"{RecordDeclaration}\nclass Point {{\n  int x;\n  int y;\n  new(int x, int y) {{\n    this.x = x;\n    this.y = y;\n  }}\n}}\nvar p = new Point(1, 2);\nrecord(p.x);\nrecord(p.y);");

    Assert.Equal(2, recorded.Count);
    Assert.Equal(1L, Assert.IsType<IntValue>(recorded[0]).Value);
    Assert.Equal(2L, Assert.IsType<IntValue>(recorded[1]).Value);
  }

  [Fact]
  public void Evaluate_FieldAssignment_MutatesInstanceState()
  {
    var recorded = Run($"{RecordDeclaration}\nclass Counter {{\n  int count;\n  new() {{\n    this.count = 0;\n  }}\n}}\nvar c = new Counter();\nc.count = c.count + 1;\nc.count = c.count + 1;\nrecord(c.count);");

    var value = Assert.IsType<IntValue>(Assert.Single(recorded));
    Assert.Equal(2L, value.Value);
  }

  [Fact]
  public void Evaluate_MethodCall_RunsBodyWithThisBoundToInstance()
  {
    var recorded = Run($"{RecordDeclaration}\nclass Greeter {{\n  string name;\n  new(string name) {{\n    this.name = name;\n  }}\n  function string greet() {{\n    return \"hello \" + this.name;\n  }}\n}}\nvar g = new Greeter(\"world\");\nrecord(g.greet());");

    var value = Assert.IsType<StringValue>(Assert.Single(recorded));
    Assert.Equal("hello world", value.Value);
  }

  [Fact]
  public void Evaluate_ClassWithoutConstructor_ConstructsWithNoArguments()
  {
    var recorded = Run($"{RecordDeclaration}\nclass Empty {{\n}}\nvar e = new Empty();\nrecord(e);");

    Assert.IsType<InstanceValue>(Assert.Single(recorded));
  }

  [Fact]
  public void Evaluate_SubclassInheritsSuperclassMethods()
  {
    var recorded = Run($"{RecordDeclaration}\nclass Animal {{\n  function string speak() {{\n    return \"...\";\n  }}\n}}\nclass Dog extends Animal {{\n}}\nvar d = new Dog();\nrecord(d.speak());");

    var value = Assert.IsType<StringValue>(Assert.Single(recorded));
    Assert.Equal("...", value.Value);
  }

  [Fact]
  public void Evaluate_ConstructorCalledWithWrongArgumentCount_ThrowsRuntimeException()
  {
    var exception = Assert.Throws<RuntimeException>(() =>
      Run($"{RecordDeclaration}\nclass Point {{\n  new(int x, int y) {{\n  }}\n}}\nvar p = new Point(1);"));

    Assert.Contains("Expected 2 argument", exception.Message);
  }

  [Fact]
  public void Evaluate_AccessingUndefinedProperty_ThrowsRuntimeException()
  {
    var exception = Assert.Throws<RuntimeException>(() =>
      Run($"{RecordDeclaration}\nclass Empty {{\n}}\nvar e = new Empty();\nrecord(e.missing);"));

    Assert.Contains("Undefined property 'missing'", exception.Message);
  }
}
