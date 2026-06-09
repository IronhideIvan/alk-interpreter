using System.Collections.Generic;
using System.IO;
using ALKScript.Interpreter.Common.Evaluation.Values;
using Tests.ALKScript.Interpreter.Runtime.Support;

namespace Tests.ALKScript.Interpreter.Runtime;

/// <summary>
/// Full end-to-end tests that run complete multi-module ALKScript programs
/// stored under <c>ALKScripts/</c> through the real pipeline and assert on
/// observed side effects (console output captured via the native "log" binding).
/// </summary>
public class EndToEndTests : RuntimeTestBase
{
  private static readonly string ScriptsDir = Path.Combine(
    Path.GetDirectoryName(typeof(EndToEndTests).Assembly.Location)!,
    "ALKScripts");

  private static string ReadScript(string testFolder, string relativePath) =>
    File.ReadAllText(Path.Combine(ScriptsDir, testFolder, relativePath));

  // ── Animal Showcase ───────────────────────────────────────────────────────

  [Fact]
  public void AnimalShowcase_InheritanceAndNativeLogging_ProducesExpectedOutput()
  {
    // The program spans two modules:
    //   main.alk      — entry point; defines announce(); instantiates Dog, Cat, GuideDog
    //   animals.alk   — exports the Animal hierarchy (abstract base + 3 concrete classes)
    //
    // Logging is provided by the "console" core module (a single native function
    // declaration) whose implementation is injected via NativeBindings below.
    //
    // Features exercised:
    //   - Named imports from a core module and from a relative file module
    //   - Abstract class with an abstract method
    //   - Two-level override: Dog overrides Animal.speak()
    //   - Three-level chain: GuideDog overrides Dog.speak() and chains base.speak()
    //   - Polymorphic dispatch: announce(Animal) calls speak() on Dog/Cat/GuideDog
    //   - base() constructor calls up the chain
    //   - Direct method call on a concrete type

    var logged = new List<string>();

    var runtime = CreateRuntimeForEvaluation(
      files: new Dictionary<string, string>
      {
        ["main.alk"]    = ReadScript("AnimalShowcase", "main.alk"),
        ["animals.alk"] = ReadScript("AnimalShowcase", "animals.alk"),
      },
      coreModules: new Dictionary<string, string>
      {
        ["console"] = ReadScript("AnimalShowcase", "console.alk"),
      });

    runtime.NativeBindings["log"] = args =>
    {
      logged.Add(((StringValue)args[0]).Value);
      return NullValue.Instance;
    };

    runtime.RunUntilComplete(runtime.RunFromFile("main.alk"));

    Assert.Equal(
      new[]
      {
        "--- Animal Showcase ---",
        "[Rex] Rex barks: Woof!",
        "[Whiskers] Whiskers meows: Meow!",
        "[Buddy] Buddy barks: Woof! (guides Alice)",
        "Direct: Buddy barks: Woof! (guides Alice)",
        "--- Done ---",
      },
      logged);
  }

  // ── Item Processor ────────────────────────────────────────────────────────

  [Fact]
  public void ItemProcessor_AsyncForLoopAndNativeBuffer_ProducesExpectedOutput()
  {
    // The program is a single-module script that:
    //   - imports "log" from the "console" core module (native free function)
    //   - imports "Buffer" from the "io" core module (native class with native methods)
    //   - uses a for loop over a fixed list:
    //       * "SKIP" items are skipped with continue
    //       * "STOP" causes an early exit via break
    //       * all other items are processed with an async function and written to a Buffer
    //   - drains the Buffer with a while loop and logs each entry
    //
    // Features exercised:
    //   - async function declaration and await expression
    //   - for loop with both break and continue
    //   - while loop for iteration
    //   - native class (Buffer) with three native instance methods
    //   - instance state persisted across method calls via instance fields

    var logged = new List<string>();

    var runtime = CreateRuntimeForEvaluation(
      files: new Dictionary<string, string>
      {
        ["main.alk"] = ReadScript("ItemProcessor", "main.alk"),
      },
      coreModules: new Dictionary<string, string>
      {
        ["console"] = ReadScript("ItemProcessor", "console.alk"),
        ["io"]      = ReadScript("ItemProcessor", "io.alk"),
      });

    runtime.NativeBindings["log"] = args =>
    {
      logged.Add(((StringValue)args[0]).Value);
      return NullValue.Instance;
    };

    // Buffer backing: items are stored as an ArrayValue in instance.Fields so
    // the three native methods share the same list for the lifetime of each instance.
    static List<ALKScriptValue> GetItems(InstanceValue instance)
    {
      if (!instance.Fields.TryGetValue("_items", out var existing))
      {
        var arr = new ArrayValue(new List<ALKScriptValue>());
        instance.Fields["_items"] = arr;
        return arr.Items;
      }
      return ((ArrayValue)existing).Items;
    }

    runtime.NativeMethodBindings["Buffer", "write"] = (instance, args) =>
    {
      GetItems(instance).Add(args[0]);
      return NullValue.Instance;
    };

    runtime.NativeMethodBindings["Buffer", "read"] = (instance, args) =>
    {
      var index = (int)((IntValue)args[0]).Value;
      return GetItems(instance)[index];
    };

    runtime.NativeMethodBindings["Buffer", "size"] = (instance, args) =>
      new IntValue(GetItems(instance).Count);

    runtime.RunUntilComplete(runtime.RunFromFile("main.alk"));

    Assert.Equal(
      new[]
      {
        "--- Results ---",
        "[processed] apple",
        "[processed] banana",
        "[processed] cherry",
        "--- Done ---",
      },
      logged);
  }
}
