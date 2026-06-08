using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Evaluator;

namespace Tests.ALKScript.Interpreter.Evaluator;

public class NativeBindingTests : EvaluatorTestBase
{
  [Fact]
  public void Evaluate_NativeFunctionWithRegisteredBinding_InvokesHostImplementation()
  {
    var calls = new List<string>();

    RunWithBindings(
      "native function void log(string message);\nlog(\"hi\");\nlog(\"there\");",
      new ScriptNativeBindings
      {
        ["log"] = arguments =>
        {
          calls.Add(((StringValue)arguments[0]).Value);
          return NullValue.Instance;
        }
      });

    Assert.Equal(new[] { "hi", "there" }, calls);
  }

  [Fact]
  public void Evaluate_NativeFunctionReturnValue_FlowsBackToCaller()
  {
    var recorded = Run(
      $"{RecordDeclaration}\nnative function int double(int n);\nrecord(double(21));",
      new ScriptNativeBindings
      {
        ["double"] = arguments => new IntValue(((IntValue)arguments[0]).Value * 2)
      });

    var value = Assert.IsType<IntValue>(Assert.Single(recorded));
    Assert.Equal(42L, value.Value);
  }

  [Fact]
  public void Evaluate_NativeMethodWithRegisteredBinding_InvokesHostImplementationBoundToInstance()
  {
    var calls = new List<string>();

    RunWithBindings(
      "class Console {\n  public native function void log(string message);\n}\nvar console = new Console();\nconsole.log(\"from instance\");",
      new ScriptNativeBindings
      {
        ["log"] = arguments =>
        {
          calls.Add(((StringValue)arguments[0]).Value);
          return NullValue.Instance;
        }
      });

    Assert.Equal(new[] { "from instance" }, calls);
  }

  [Fact]
  public void Evaluate_NativeFunctionWithoutRegisteredBinding_ThrowsRuntimeExceptionAtDeclaration()
  {
    var exception = Assert.Throws<RuntimeException>(() =>
      RunWithBindings("native function void log(string message);", new ScriptNativeBindings()));

    Assert.Contains("Native function 'log' has no host implementation registered", exception.Message);
  }

  private static IReadOnlyList<ALKScriptValue> Run(string source, ScriptNativeBindings nativeBindings)
  {
    var recorded = new List<ALKScriptValue>();
    var bindings = new ScriptNativeBindings(nativeBindings)
    {
      ["record"] = arguments =>
      {
        recorded.Add(arguments[0]);
        return NullValue.Instance;
      }
    };

    RunWithBindings(source, bindings);
    return recorded;
  }
}
