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
  public void Evaluate_NativeMethodWithRegisteredBinding_InvokesHostImplementationWithReceivingInstance()
  {
    var calls = new List<(string TypeName, string Message)>();

    RunWithMethodBindings(
      "native class Console {\n  public native function void log(string message);\n}\nvar console = new Console();\nconsole.log(\"from instance\");",
      nativeBindings: null,
      nativeMethodBindings: new ScriptNativeMethodBindings
      {
        ["Console", "log"] = (instance, arguments) =>
        {
          calls.Add((instance.TypeName, ((StringValue)arguments[0]).Value));
          return NullValue.Instance;
        }
      });

    Assert.Equal(new[] { ("Console", "from instance") }, calls);
  }

  [Fact]
  public void Evaluate_NativeMethod_CanReadAndMutateTheReceivingInstancesFields()
  {
    // The defining "runtime-backed collection" scenario: a native method
    // backs a class with real, host-managed storage by reading/writing the
    // receiver's Fields directly — script code never sees how "items" is
    // represented, only that push/count behave consistently.
    var recorded = Run(
      RecordDeclaration +
      "native class Bag {\n" +
      "  public native function void push(Object item);\n" +
      "  public native function int count();\n" +
      "}\n" +
      "var bag = new Bag();\n" +
      "bag.push(\"a\");\n" +
      "bag.push(\"b\");\n" +
      "record(bag.count());\n",
      new ScriptNativeBindings(),
      new ScriptNativeMethodBindings
      {
        ["Bag", "push"] = (instance, arguments) =>
        {
          if (!instance.Fields.TryGetValue("__items", out var items) || !(items is ArrayValue array))
          {
            array = new ArrayValue(new List<ALKScriptValue>());
            instance.Fields["__items"] = array;
          }

          array.Items.Add(arguments[0]);
          return NullValue.Instance;
        },
        ["Bag", "count"] = (instance, _) =>
        {
          var count = instance.Fields.TryGetValue("__items", out var items) && items is ArrayValue array ? array.Items.Count : 0;
          return new IntValue(count);
        }
      });

    var value = Assert.IsType<IntValue>(Assert.Single(recorded));
    Assert.Equal(2L, value.Value);
  }

  [Fact]
  public void Evaluate_NativeMethodInheritedFromSuperclass_ResolvesAgainstTheDeclaringClassesBinding()
  {
    // Bindings are scoped to the class that *declares* the native method, not
    // the runtime type of the receiver — so a binding registered for the
    // superclass is found through a subclass that merely inherits it.
    var calls = new List<string>();

    RunWithMethodBindings(
      "native class Logger {\n  public native function void log(string message);\n}\n" +
      "class FileLogger extends Logger {}\n" +
      "var logger = new FileLogger();\nlogger.log(\"inherited\");",
      nativeBindings: null,
      nativeMethodBindings: new ScriptNativeMethodBindings
      {
        ["Logger", "log"] = (instance, arguments) =>
        {
          calls.Add(((StringValue)arguments[0]).Value);
          return NullValue.Instance;
        }
      });

    Assert.Equal(new[] { "inherited" }, calls);
  }

  [Fact]
  public void Evaluate_NativeMethodWithoutRegisteredBinding_ThrowsRuntimeExceptionWhenAccessed()
  {
    var exception = Assert.Throws<RuntimeException>(() =>
      RunWithMethodBindings(
        "native class Console {\n  public native function void log(string message);\n}\nvar console = new Console();\nconsole.log(\"hi\");",
        nativeBindings: null,
        nativeMethodBindings: new ScriptNativeMethodBindings()));

    Assert.Contains("Native method 'Console.log' has no host implementation registered", exception.Message);
  }

  [Fact]
  public void Evaluate_NativeFunctionWithoutRegisteredBinding_ThrowsRuntimeExceptionAtDeclaration()
  {
    var exception = Assert.Throws<RuntimeException>(() =>
      RunWithBindings("native function void log(string message);", new ScriptNativeBindings()));

    Assert.Contains("Native function 'log' has no host implementation registered", exception.Message);
  }

  private static IReadOnlyList<ALKScriptValue> Run(string source, ScriptNativeBindings nativeBindings, ScriptNativeMethodBindings? nativeMethodBindings = null)
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

    if (nativeMethodBindings == null)
    {
      RunWithBindings(source, bindings);
    }
    else
    {
      RunWithMethodBindings(source, bindings, nativeMethodBindings);
    }

    return recorded;
  }
}
