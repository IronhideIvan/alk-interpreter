using System.Collections.Generic;
using System.Collections.Generic;
using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Common.Evaluation;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Evaluator;

namespace Tests.ALKScript.Interpreter.Evaluator.Unit;

public class ClassEnvironmentsTests
{
  private static ClassValue MakeClass(string name, ScriptEnvironment closure) =>
    new ClassValue(
      new ClassDecl(false, Nodes.Identifier(name), System.Array.Empty<string>(), null, System.Array.Empty<TypeNode>(), System.Array.Empty<MemberDecl>()),
      superclass: null,
      closure);

  [Fact]
  public void For_ReturnsTheClassesCapturedClosure()
  {
    var closure = new ScriptEnvironment();
    closure.Define("fromEnclosingScope", NullValue.Instance);

    var environment = ClassEnvironments.For(MakeClass("Foo", closure));

    Assert.Same(closure, environment);
    Assert.True(environment.TryGet("fromEnclosingScope", out _));
  }

  [Fact]
  public void For_ReturnsTheSameInstanceEveryCall()
  {
    var classValue = MakeClass("Foo", new ScriptEnvironment());

    var first = ClassEnvironments.For(classValue);
    var second = ClassEnvironments.For(classValue);

    Assert.Same(first, second);
  }
}
