using System.Collections.Generic;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Runtime;

namespace Tests.ALKScript.Interpreter.Runtime;

/// <summary>
/// Tests for module-qualified native function bindings — verifying that two
/// core modules can each declare a same-named <c>native function</c> and have
/// independent host implementations, and that a module-qualified binding takes
/// precedence over an unqualified fallback.
/// </summary>
public class NativeBindingModuleTests
{
  [Fact]
  public void TwoModulesWithSameNamedNativeFunction_ResolveSeparately()
  {
    var aLog = new List<string>();
    var bLog = new List<string>();

    var runtime = new ProgramRuntime();
    runtime.CoreModules["mod-a"] = "export native function void ping(string msg);";
    runtime.CoreModules["mod-b"] = "export native function void ping(string msg);";

    runtime.NativeBindings["mod-a", "ping"] = arguments =>
    {
      aLog.Add(((StringValue)arguments[0]).Value);
      return NullValue.Instance;
    };
    runtime.NativeBindings["mod-b", "ping"] = arguments =>
    {
      bLog.Add(((StringValue)arguments[0]).Value);
      return NullValue.Instance;
    };

    runtime.RunFromSource("""
      import { ping as pingA } from "mod-a";
      import { ping as pingB } from "mod-b";
      pingA("from-a");
      pingB("from-b");
      """).RunToCompletion();

    Assert.Equal(new[] { "from-a" }, aLog);
    Assert.Equal(new[] { "from-b" }, bLog);
  }

  [Fact]
  public void ModuleQualifiedBinding_TakesPrecedenceOverUnqualifiedFallback()
  {
    var calls = new List<string>();

    var runtime = new ProgramRuntime();
    runtime.CoreModules["mymod"] = "export native function void ping(string msg);";

    runtime.NativeBindings["mymod", "ping"] = arguments =>
    {
      calls.Add("qualified:" + ((StringValue)arguments[0]).Value);
      return NullValue.Instance;
    };
    runtime.NativeBindings["ping"] = arguments =>
    {
      calls.Add("fallback:" + ((StringValue)arguments[0]).Value);
      return NullValue.Instance;
    };

    runtime.RunFromSource("""
      import { ping } from "mymod";
      ping("hello");
      """).RunToCompletion();

    Assert.Equal(new[] { "qualified:hello" }, calls);
  }
}
