using ALKScript.Interpreter.Common.Evaluation;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Evaluator;

namespace Tests.ALKScript.Interpreter.Evaluator.Unit;

public class NamesTests
{
  [Fact]
  public void LookUp_DefinedName_ReturnsItsValue()
  {
    var environment = new ScriptEnvironment();
    environment.Define("x", new IntValue(42));

    var value = Names.LookUp(Nodes.Identifier("x"), environment);

    Assert.Equal(42L, Assert.IsType<IntValue>(value).Value);
  }

  [Fact]
  public void LookUp_DefinedInEnclosingScope_ResolvesThroughChain()
  {
    var globals = new ScriptEnvironment();
    globals.Define("x", new IntValue(1));
    var local = new ScriptEnvironment(globals);

    var value = Names.LookUp(Nodes.Identifier("x"), local);

    Assert.Equal(1L, Assert.IsType<IntValue>(value).Value);
  }

  [Fact]
  public void LookUp_UndefinedName_ThrowsRuntimeExceptionNamingIt()
  {
    var environment = new ScriptEnvironment();

    var exception = Assert.Throws<RuntimeException>(() => Names.LookUp(Nodes.Identifier("missing"), environment));

    Assert.Contains("Undefined name 'missing'", exception.Message);
  }
}
