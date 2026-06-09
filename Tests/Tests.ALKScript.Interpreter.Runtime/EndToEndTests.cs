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

  private static string ReadScript(string relativePath) =>
    File.ReadAllText(Path.Combine(ScriptsDir, relativePath));

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
        ["main.alk"]    = ReadScript("main.alk"),
        ["animals.alk"] = ReadScript("animals.alk"),
      },
      coreModules: new Dictionary<string, string>
      {
        ["console"] = ReadScript("console.alk"),
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
}
