using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Common.Evaluation;
using ALKScript.Interpreter.Common.Evaluation.Values;

namespace Tests.ALKScript.Interpreter.Evaluator.Cursor;

/// <summary>
/// Step 5 coverage for the "Phase B" structural Capture/Restore plan (docs:
/// validated-nibbling-narwhal): <see cref="ScriptEnvironment"/>'s "own scope"
/// internal accessors, used by Capture so a nested scope's DTO doesn't
/// duplicate inherited context from its enclosing scopes
/// (docs/ASYNC_AWAIT_DESIGN.md Addendum 3).
/// </summary>
public class ScriptEnvironmentInternalAccessorsTests
{
  private static TypeNode IntType => new TypeNode("int", System.Array.Empty<TypeNode>(), 0, false);

  [Fact]
  public void Enclosing_ReturnsTheEnclosingScope_OrNullForRoot()
  {
    var root = new ScriptEnvironment();
    var child = new ScriptEnvironment(root);

    Assert.Null(root.Enclosing);
    Assert.Same(root, child.Enclosing);
  }

  [Fact]
  public void OwnTypesAndConsts_ReflectOnlyThisScopesDefinitions()
  {
    var root = new ScriptEnvironment();
    root.Define("x", NullValue.Instance, IntType, isConst: true);

    var child = new ScriptEnvironment(root);
    child.Define("y", NullValue.Instance);

    Assert.True(root.OwnTypes.ContainsKey("x"));
    Assert.Equal("int", root.OwnTypes["x"]!.Name);
    Assert.Contains("x", root.OwnConsts);

    Assert.False(child.OwnTypes.ContainsKey("x"));
    Assert.DoesNotContain("x", child.OwnConsts);
    Assert.True(child.OwnTypes.ContainsKey("y"));
  }

  [Fact]
  public void OwnCurrentClassAndRelated_AreNullOnAScopeThatDoesNotSetThem_EvenWhenAnEnclosingScopeDoes()
  {
    var root = new ScriptEnvironment();
    root.CurrentClass = null;
    root.CurrentFunctionReturnType = IntType;
    root.IsInConstructor = true;

    var child = new ScriptEnvironment(root);

    Assert.Equal(IntType.Name, root.OwnCurrentFunctionReturnType!.Name);
    Assert.True(root.OwnIsInConstructor);
    Assert.Null(root.OwnCurrentClass);
    Assert.Null(root.OwnCurrentTypeArguments);

    Assert.Null(child.OwnCurrentFunctionReturnType);
    Assert.False(child.OwnIsInConstructor);

    // Walked-up resolution still sees the enclosing scope's values.
    Assert.Equal(IntType.Name, child.CurrentFunctionReturnType!.Name);
    Assert.True(child.IsInConstructor);
  }
}
