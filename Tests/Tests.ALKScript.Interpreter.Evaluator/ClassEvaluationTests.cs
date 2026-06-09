using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Evaluator;

namespace Tests.ALKScript.Interpreter.Evaluator;

public class ClassEvaluationTests : EvaluatorTestBase
{
  [Fact]
  public void Evaluate_NewExpression_ConstructsInstanceAndRunsConstructor()
  {
    var recorded = Run($"{RecordDeclaration}\nclass Point {{\n  public int x;\n  public int y;\n  new(int x, int y) {{\n    this.x = x;\n    this.y = y;\n  }}\n}}\nvar p = new Point(1, 2);\nrecord(p.x);\nrecord(p.y);");

    Assert.Equal(2, recorded.Count);
    Assert.Equal(1L, Assert.IsType<IntValue>(recorded[0]).Value);
    Assert.Equal(2L, Assert.IsType<IntValue>(recorded[1]).Value);
  }

  [Fact]
  public void Evaluate_FieldAssignment_MutatesInstanceState()
  {
    var recorded = Run($"{RecordDeclaration}\nclass Counter {{\n  public int count;\n  new() {{\n    this.count = 0;\n  }}\n}}\nvar c = new Counter();\nc.count = c.count + 1;\nc.count = c.count + 1;\nrecord(c.count);");

    var value = Assert.IsType<IntValue>(Assert.Single(recorded));
    Assert.Equal(2L, value.Value);
  }

  [Fact]
  public void Evaluate_MethodCall_RunsBodyWithThisBoundToInstance()
  {
    var recorded = Run($"{RecordDeclaration}\nclass Greeter {{\n  string name;\n  new(string name) {{\n    this.name = name;\n  }}\n  public function string greet() {{\n    return \"hello \" + this.name;\n  }}\n}}\nvar g = new Greeter(\"world\");\nrecord(g.greet());");

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
    var recorded = Run($"{RecordDeclaration}\nclass Animal {{\n  public function string speak() {{\n    return \"...\";\n  }}\n}}\nclass Dog extends Animal {{\n}}\nvar d = new Dog();\nrecord(d.speak());");

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

  // ── Field declaration initializers ───────────────────────────────────────

  [Fact]
  public void Evaluate_FieldWithInitializer_IsSetBeforeConstructorRuns()
  {
    // Field initializers run before the constructor body, so the constructor
    // can rely on them being already set.
    var recorded = Run(
      $"{RecordDeclaration}\n" +
      "class Widget {\n" +
      "  public int count = 10;\n" +
      "  new() {\n" +
      "    this.count = this.count + 5;\n" +
      "  }\n" +
      "}\n" +
      "var w = new Widget();\n" +
      "record(w.count);");

    // count starts at 10 (initializer) then constructor adds 5 → 15
    Assert.Equal(15L, Assert.IsType<IntValue>(Assert.Single(recorded)).Value);
  }

  [Fact]
  public void Evaluate_FieldWithoutInitializer_DefaultsToNull()
  {
    var recorded = Run(
      $"{RecordDeclaration}\n" +
      "class Node {\n" +
      "  public var next;\n" +
      "}\n" +
      "var n = new Node();\n" +
      "record(n.next);");

    Assert.IsType<NullValue>(Assert.Single(recorded));
  }

  [Fact]
  public void Evaluate_DerivedClassFieldInitializers_InitializedInBaseFirstOrder()
  {
    var recorded = Run(
      $"{RecordDeclaration}\n" +
      "class Base {\n" +
      "  public int x = 1;\n" +
      "}\n" +
      "class Derived extends Base {\n" +
      "  public int y = 2;\n" +
      "}\n" +
      "var d = new Derived();\n" +
      "record(d.x);\n" +
      "record(d.y);");

    Assert.Equal(2, recorded.Count);
    Assert.Equal(1L, Assert.IsType<IntValue>(recorded[0]).Value);
    Assert.Equal(2L, Assert.IsType<IntValue>(recorded[1]).Value);
  }

  // ── Access modifier enforcement ───────────────────────────────────────────

  [Fact]
  public void Evaluate_AccessingPrivateMemberFromOutsideClass_ThrowsRuntimeException()
  {
    var exception = Assert.Throws<RuntimeException>(() =>
      Run(
        $"{RecordDeclaration}\n" +
        "class Secret { private int code = 42; }\n" +
        "var s = new Secret();\n" +
        "record(s.code);"));

    Assert.Contains("private", exception.Message);
    Assert.Contains("code", exception.Message);
  }

  [Fact]
  public void Evaluate_AccessingPrivateMemberFromInsideClass_IsAllowed()
  {
    var recorded = Run(
      $"{RecordDeclaration}\n" +
      "class Secret {\n" +
      "  private int code = 42;\n" +
      "  public function int getCode() { return this.code; }\n" +
      "}\n" +
      "var s = new Secret();\n" +
      "record(s.getCode());");

    Assert.Equal(42L, Assert.IsType<IntValue>(Assert.Single(recorded)).Value);
  }

  [Fact]
  public void Evaluate_AccessingProtectedMemberFromSubclass_IsAllowed()
  {
    var recorded = Run(
      $"{RecordDeclaration}\n" +
      "class Animal {\n" +
      "  protected string sound = \"...\";\n" +
      "}\n" +
      "class Dog extends Animal {\n" +
      "  public function string bark() { return this.sound; }\n" +
      "}\n" +
      "var d = new Dog();\n" +
      "record(d.bark());");

    Assert.Equal("...", Assert.IsType<StringValue>(Assert.Single(recorded)).Value);
  }

  [Fact]
  public void Evaluate_AccessingProtectedMemberFromOutsideHierarchy_ThrowsRuntimeException()
  {
    var exception = Assert.Throws<RuntimeException>(() =>
      Run(
        $"{RecordDeclaration}\n" +
        "class Animal { protected string sound = \"...\"; }\n" +
        "var a = new Animal();\n" +
        "record(a.sound);"));

    Assert.Contains("protected", exception.Message);
    Assert.Contains("sound", exception.Message);
  }

  [Fact]
  public void Evaluate_WritingToPrivateFieldFromOutsideClass_ThrowsRuntimeException()
  {
    var exception = Assert.Throws<RuntimeException>(() =>
      Run(
        "class Counter { private int n = 0; }\n" +
        "var c = new Counter();\n" +
        "c.n = 99;"));

    Assert.Contains("private", exception.Message);
    Assert.Contains("n", exception.Message);
  }
}
