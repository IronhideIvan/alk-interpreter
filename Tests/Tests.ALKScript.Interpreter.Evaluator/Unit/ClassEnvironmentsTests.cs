using System.Collections.Generic;
using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Evaluator;

namespace Tests.ALKScript.Interpreter.Evaluator.Unit;

public class ClassEnvironmentsTests
{
  private static ClassValue MakeClass(string name) =>
    new ClassValue(
      new ClassDecl(false, Nodes.Identifier(name), System.Array.Empty<string>(), null, System.Array.Empty<TypeNode>(), System.Array.Empty<MemberDecl>()),
      superclass: null);

  [Fact]
  public void For_ReturnsAnEmptyScope()
  {
    var environment = ClassEnvironments.For(MakeClass("Foo"));

    Assert.False(environment.TryGet("anything", out _));
  }

  [Fact]
  public void For_ReturnsAFreshScopePerCall()
  {
    var classValue = MakeClass("Foo");

    var first = ClassEnvironments.For(classValue);
    var second = ClassEnvironments.For(classValue);

    Assert.NotSame(first, second);
  }
}
