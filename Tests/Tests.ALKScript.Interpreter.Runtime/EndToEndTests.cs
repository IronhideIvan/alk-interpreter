using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ALKScript.Interpreter.Common.Evaluation.Scheduling;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Evaluator;
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

    // OperationBinder resolves 'native async function process': it prepends a
    // tag and completes synchronously, so the test doesn't need a real async
    // host but still exercises the full await/scheduler path.
    runtime.OperationBinder = new LambdaOperationBinder(op =>
    {
      if (op.Name == "process")
      {
        var name = ((StringValue)op.Arguments[0]).Value;
        return Task.FromResult<ALKScriptValue>(new StringValue("[processed] " + name));
      }
      throw new InvalidOperationException($"Unknown async operation: '{op.Name}'.");
    });

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

  // ── Async Fetcher ─────────────────────────────────────────────────────────

  [Fact]
  public async Task AsyncFetcher_NativeAsyncMethodOnClassInstance_ProducesExpectedOutput()
  {
    // The program spans two core modules:
    //   network.alk   — declares a native class HttpClient with a
    //                   'public native async function string get(string url)'
    //   console.alk   — declares 'native function void log(string message)'
    //
    // main.alk creates one HttpClient instance and awaits get() three times
    // inside a for loop, logging each resolved response.
    //
    // This test focuses on the 'native async method on a class instance' path:
    //   - The host registers the binding via NativeMethodBindings["HttpClient","get"].
    //   - The binding returns a TaskValue wrapping Task.FromResult (synchronously
    //     completing, so the test is deterministic without a real scheduler).
    //   - The script suspends on each 'await client.get(url)', letting the
    //     scheduler settle the task and resume with the string result.
    //   - The same instance is reused across all three calls, confirming that
    //     the bound 'this' is threaded through correctly for every invocation.

    var logged = new List<string>();

    var runtime = CreateRuntimeForEvaluation(
      files: new Dictionary<string, string>
      {
        ["main.alk"] = ReadScript("AsyncFetcher", "main.alk"),
      },
      coreModules: new Dictionary<string, string>
      {
        ["console"] = ReadScript("AsyncFetcher", "console.alk"),
        ["network"] = ReadScript("AsyncFetcher", "network.alk"),
      });

    runtime.NativeBindings["log"] = args =>
    {
      logged.Add(((StringValue)args[0]).Value);
      return NullValue.Instance;
    };

    // Each call returns a genuinely pending Task (Task.Run + Task.Delay) so
    // the script truly suspends on each 'await client.get(url)' and the
    // scheduler must pump multiple times before each continuation arrives from
    // the background thread — mirroring a real environment where the host is
    // performing slow I/O.  The result is still deterministic: the three URLs
    // are awaited in sequence, so their responses land in insertion order.
    runtime.NativeMethodBindings["HttpClient", "get"] = (instance, args) =>
    {
      var url = ((StringValue)args[0]).Value;
      return new TaskValue(Task.Run(async () =>
      {
        await Task.Delay(10);
        return (ALKScriptValue)new StringValue("[200] " + url);
      }));
    };

    // Drive the script via Pump() — one call per simulated game tick — rather
    // than RunUntilComplete.  Between ticks we yield the thread briefly so the
    // background Task.Delay completions can post their continuations onto the
    // scheduler queue; Pump() then picks them up on the next tick.  This is
    // exactly how a real game host would integrate the script runtime.
    var evaluation = runtime.RunFromFile("main.alk");
    while (!evaluation.IsCompleted)
    {
      runtime.Pump();
      await Task.Delay(5, TestContext.Current.CancellationToken);
    }
    runtime.Pump(); // drain any continuations queued in the final tick

    Assert.Equal(
      new[]
      {
        "[200] api/users",
        "[200] api/posts",
        "[200] api/comments",
        "--- Done ---",
      },
      logged);
  }

  // ── Pump Ordering ─────────────────────────────────────────────────────────

  [Fact]
  public void PumpOrdering_ScriptSuspendsAtEachAwait_LogsIncrementallyAcrossPumps()
  {
    // Verifies that execution genuinely stops at each 'await' and only
    // advances when the host calls Pump() — the core contract for game-loop
    // integration.
    //
    // Each 'sensor.read()' binding captures its own TaskCompletionSource so
    // the test controls exactly when each async operation settles, with no
    // timing dependency.  Because ScheduledTask uses ExecuteSynchronously on
    // its ContinueWith, calling TCS.SetResult enqueues the script continuation
    // synchronously before SetResult returns — so by the time Pump() is called
    // the continuation is already in the queue and runs deterministically.
    //
    // Checkpoint 0 — after RunFromFile, before any Pump:
    //   The synchronous prefix of the script (log("starting")) has already run
    //   on the calling thread.  The first 'await sensor.read()' has suspended
    //   evaluation; nothing beyond it has executed yet.
    //
    // Checkpoint 1 — after settling read #1 and one Pump:
    //   The continuation resumes, logs "reading-1: 42", then suspends again at
    //   the second 'await sensor.read()'.  Exactly one new log line appears.
    //
    // Checkpoint 2 — after settling read #2 and one Pump:
    //   The script runs to completion: "reading-2: 99" and "done" are logged in
    //   the same pump and evaluation.IsCompleted flips to true.

    var logged = new List<string>();
    var pendingReads = new List<TaskCompletionSource<ALKScriptValue>>();

    var runtime = CreateRuntimeForEvaluation(
      files: new Dictionary<string, string>
      {
        ["main.alk"] = ReadScript("PumpOrdering", "main.alk"),
      },
      coreModules: new Dictionary<string, string>
      {
        ["console"] = ReadScript("PumpOrdering", "console.alk"),
        ["sensor"]  = ReadScript("PumpOrdering", "sensor.alk"),
      });

    runtime.NativeBindings["log"] = args =>
    {
      logged.Add(((StringValue)args[0]).Value);
      return NullValue.Instance;
    };

    runtime.NativeMethodBindings["Sensor", "read"] = (instance, args) =>
    {
      var tcs = new TaskCompletionSource<ALKScriptValue>();
      pendingReads.Add(tcs);
      return new TaskValue(tcs.Task);
    };

    // ── Checkpoint 0 ──────────────────────────────────────────────────────
    // RunFromFile starts the evaluator, which runs synchronously until the
    // first 'await sensor.read()' suspends it.  The pre-await log("starting")
    // has already executed on this thread; nothing after the first await has.
    var evaluation = runtime.RunFromFile("main.alk");

    Assert.Equal(new[] { "starting" }, logged);
    Assert.False(evaluation.IsCompleted);

    // ── Checkpoint 1 ──────────────────────────────────────────────────────
    // Settle read #1: the TCS continuation is synchronously enqueued onto the
    // scheduler before SetResult returns.  Pump() drains it: the script logs
    // "reading-1: 42", then suspends again at the second await.
    pendingReads[0].SetResult(new IntValue(42));
    runtime.Pump();

    Assert.Equal(new[] { "starting", "reading-1: 42" }, logged);
    Assert.False(evaluation.IsCompleted);

    // ── Checkpoint 2 ──────────────────────────────────────────────────────
    // Settle read #2: the script now runs to the end in one Pump — logging
    // "reading-2: 99" and "done" with no further awaits in between.
    pendingReads[1].SetResult(new IntValue(99));
    runtime.Pump();

    Assert.Equal(new[] { "starting", "reading-1: 42", "reading-2: 99", "done" }, logged);
    Assert.True(evaluation.IsCompleted);
  }

  // ── Field declaration initializers ───────────────────────────────────────

  [Fact]
  public void FieldDeclInit_InitializersRunBeforeConstructorBodyAndInheritanceOrderIsRespected()
  {
    // The program spans two modules:
    //   main.alk      — entry point; creates Item and PremiumItem instances
    //   inventory.alk — exports Item (base) and PremiumItem (derived)
    //
    // Features exercised:
    //   - Field with a string initializer ("general") is set before the constructor body runs
    //   - Field with a numeric initializer (0) is set before the constructor body runs
    //   - Field without an initializer (note) is null until the constructor assigns it
    //   - Constructor body captures the initialized values via getNote(), proving order
    //   - Derived class inherits the base class field initializers (category="general")
    //   - Derived class has its own field initializer (tier="premium")
    //   - Constructor can overwrite an initializer value (bonus: 50 → 99)
    //   - Mutable public fields can be updated after construction (stock: 0 → 10)

    var logged = new List<string>();

    var runtime = CreateRuntimeForEvaluation(
      files: new Dictionary<string, string>
      {
        ["main.alk"]      = ReadScript("FieldDeclInit", "main.alk"),
        ["inventory.alk"] = ReadScript("FieldDeclInit", "inventory.alk"),
      },
      coreModules: new Dictionary<string, string>
      {
        ["console"] = ReadScript("FieldDeclInit", "console.alk"),
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
        // Base Item: constructor snapshot proves initializers ran first
        "label=Widget category=general stock=0",
        // Public fields readable after construction
        "general",
        "0",
        // Field is mutable after construction
        "10",
        // PremiumItem: base field initializer inherited
        "general",
        // PremiumItem: derived field initializer
        "premium",
        // PremiumItem: constructor overwrote derived initializer value (50 → 99)
        "99",
        "done",
      },
      logged);
  }

  // ── Foreach over arrays ───────────────────────────────────────────────────

  [Fact]
  public void ForeachShowcase_LoopsOverArraysWithBreakAndContinue_ProducesExpectedOutput()
  {
    // The program is a single-module script that:
    //   - sums a numeric array with 'foreach', skipping negatives via
    //     'continue' and stopping early via 'break' once the running total
    //     exceeds 100
    //   - iterates a string array with 'foreach', logging each element
    //   - iterates an empty array with 'foreach', confirming the body never runs
    //
    // Features exercised:
    //   - foreach (var x in collection) { ... } over array literals
    //   - break and continue inside a foreach body
    //   - foreach over an empty array

    var logged = new List<string>();

    var runtime = CreateRuntimeForEvaluation(
      files: new Dictionary<string, string>
      {
        ["main.alk"] = ReadScript("ForeachShowcase", "main.alk"),
      },
      coreModules: new Dictionary<string, string>
      {
        ["console"] = ReadScript("ForeachShowcase", "console.alk"),
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
        "total=150",
        "fruit: apple",
        "fruit: banana",
        "fruit: cherry",
        "empty-count=0",
        "done",
      },
      logged);
  }

  // ── Switch statement ────────────────────────────────────────────────────────

  [Fact]
  public void SwitchShowcase_GroupedCasesDefaultAndFallThrough_ProducesExpectedOutput()
  {
    // The program is a single-module script that:
    //   - uses 'switch' inside describe(int) where 'case 2' has an empty body
    //     (falling through to 'case 3') and a 'default' branch handles the
    //     unmatched value, returning directly from each branch
    //   - uses 'switch' inside process(int) where 'case 1' has no 'break' and
    //     falls through into 'case 2', while the other cases use 'break'
    //
    // Features exercised:
    //   - switch (expr) { case ...: ... default: ... }
    //   - grouped case labels sharing one body via fall-through
    //   - fall-through across cases when 'break' is omitted
    //   - 'break' terminating a case
    //   - 'default' branch for unmatched values
    //   - 'return' from inside a switch case

    var logged = new List<string>();

    var runtime = CreateRuntimeForEvaluation(
      files: new Dictionary<string, string>
      {
        ["main.alk"] = ReadScript("SwitchShowcase", "main.alk"),
      },
      coreModules: new Dictionary<string, string>
      {
        ["console"] = ReadScript("SwitchShowcase", "console.alk"),
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
        "one",
        "two-or-three",
        "two-or-three",
        "other",
        "case-1",
        "case-2",
        "case-2",
        "case-3",
        "case-default",
        "done",
      },
      logged);
  }

  // ── Array methods ────────────────────────────────────────────────────────

  [Fact]
  public void ArrayMethodsShowcase_PushPopRemoveJoinSliceAndLength_ProducesExpectedOutput()
  {
    // The program is a single-module script that exercises every member of
    // the built-in array API in sequence on a shared array, then on arrays
    // derived from it:
    //   - 'length' as a property
    //   - 'push' (mutates, returns the new length)
    //   - 'pop' (mutates, returns the removed last element)
    //   - 'remove' (mutates, returns the removed element at an index)
    //   - 'join' (pure, returns a new array combining two arrays)
    //   - 'slice' (pure, returns a new sub-array)
    //
    // Features exercised:
    //   - array.length
    //   - array.push(value), array.pop(), array.remove(index)
    //   - array.join(other), array.slice(start, count)
    //   - 'join' and 'slice' do not mutate their receivers

    var logged = new List<string>();

    var runtime = CreateRuntimeForEvaluation(
      files: new Dictionary<string, string>
      {
        ["main.alk"] = ReadScript("ArrayMethodsShowcase", "main.alk"),
      },
      coreModules: new Dictionary<string, string>
      {
        ["console"] = ReadScript("ArrayMethodsShowcase", "console.alk"),
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
        "length=3",
        "push-returned=4",
        "after-push-length=4",
        "items[3]=4",
        "popped=4",
        "after-pop-length=3",
        "removed=1",
        "after-remove-length=2",
        "items[0]=2",
        "combined.length=4",
        "items.length=2",
        "middle.length=2",
        "middle[0]=3",
        "middle[1]=10",
        "combined.length=4",
        "done",
      },
      logged);
  }

  // ── Bitwise operators and string interpolation ──────────────────────────────

  [Fact]
  public void BitwiseAndInterpolationShowcase_OperatorsAndTemplateLiterals_ProducesExpectedOutput()
  {
    // The program is a single-module script that exercises:
    //   - bitwise operators: '&', '|', '^', '~', '<<', '>>'
    //   - compound bitwise assignment: '&=', '|=', '^=', '<<=', '>>='
    //   - template-literal string interpolation: `text ${expr} text`,
    //     including plain (non-interpolated) templates and templates with
    //     multiple/expression interpolations

    var logged = new List<string>();

    var runtime = CreateRuntimeForEvaluation(
      files: new Dictionary<string, string>
      {
        ["main.alk"] = ReadScript("BitwiseAndInterpolationShowcase", "main.alk"),
      },
      coreModules: new Dictionary<string, string>
      {
        ["console"] = ReadScript("BitwiseAndInterpolationShowcase", "console.alk"),
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
        "a & b = 2",
        "a | b = 7",
        "a ^ b = 5",
        "~a = -7",
        "a << 2 = 24",
        "a >> 1 = 3",
        "flags &= 3 -> 2",
        "flags |= 8 -> 10",
        "flags ^= 5 -> 15",
        "flags <<= 2 -> 60",
        "flags >>= 1 -> 30",
        "Hello, Ada!",
        "Ada scored 95 points.",
        "Plain string with no interpolation",
        "Sum: 3 and product: 12",
        "done",
      },
      logged);
  }

  // ── Namespace imports ────────────────────────────────────────────────────────

  [Fact]
  public void NamespaceImportShowcase_StarAsImport_ExposesModuleMembersAsNamespace()
  {
    // The program spans two modules:
    //   main.alk      — entry point; imports "* as MathUtils" from "./mathutils"
    //   mathutils.alk — exports two functions and a variable
    //
    // Features exercised:
    //   - "import * as N from '...'" namespace imports
    //   - accessing functions and variables as members of the namespace object

    var logged = new List<string>();

    var runtime = CreateRuntimeForEvaluation(
      files: new Dictionary<string, string>
      {
        ["main.alk"]      = ReadScript("NamespaceImportShowcase", "main.alk"),
        ["mathutils.alk"] = ReadScript("NamespaceImportShowcase", "mathutils.alk"),
      },
      coreModules: new Dictionary<string, string>
      {
        ["console"] = ReadScript("NamespaceImportShowcase", "console.alk"),
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
        "square(4) = 16",
        "cube(3) = 27",
        "pi = 3",
        "done",
      },
      logged);
  }

  // ── Enums ───────────────────────────────────────────────────────────────────

  [Fact]
  public void EnumShowcase_SwitchAndEquality_BehaveAsExpected()
  {
    // Features exercised:
    //   - "enum" declarations with implicit and explicit member values
    //   - "Enum.Member" access, "switch" matching on enum members
    //   - "==" equality between enum members

    var logged = new List<string>();

    var runtime = CreateRuntimeForEvaluation(
      files: new Dictionary<string, string>
      {
        ["main.alk"] = ReadScript("EnumShowcase", "main.alk"),
      },
      coreModules: new Dictionary<string, string>
      {
        ["console"] = ReadScript("EnumShowcase", "console.alk"),
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
        "red",
        "blue",
        "cyan",
        "Green = Color.Green",
        "Cyan = Color.Cyan",
        "true",
        "false",
        "done",
      },
      logged);
  }

  // ── Interfaces ──────────────────────────────────────────────────────────────

  [Fact]
  public void InterfaceShowcase_ImplementingClasses_SatisfyInterfaceContract()
  {
    // Features exercised:
    //   - "interface" declarations with method signatures only
    //   - "implements" on a class, validated at declaration time
    //   - polymorphic dispatch through an interface-shaped collection

    var logged = new List<string>();

    var runtime = CreateRuntimeForEvaluation(
      files: new Dictionary<string, string>
      {
        ["main.alk"] = ReadScript("InterfaceShowcase", "main.alk"),
      },
      coreModules: new Dictionary<string, string>
      {
        ["console"] = ReadScript("InterfaceShowcase", "console.alk"),
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
        "circle area = 12.56636",
        "square area = 9",
        "done",
      },
      logged);
  }

  // ── Sealed classes ──────────────────────────────────────────────────────────

  [Fact]
  public void SealedClassShowcase_ExtendingSealedClass_ThrowsAtDeclaration()
  {
    // A "sealed" class cannot be extended; the subclass declaration fails as
    // soon as it is executed, before any of the program's other statements run.

    var logged = new List<string>();

    var runtime = CreateRuntimeForEvaluation(
      files: new Dictionary<string, string>
      {
        ["main.alk"] = ReadScript("SealedClassShowcase", "main.alk"),
      },
      coreModules: new Dictionary<string, string>
      {
        ["console"] = ReadScript("SealedClassShowcase", "console.alk"),
      });

    runtime.NativeBindings["log"] = args =>
    {
      logged.Add(((StringValue)args[0]).Value);
      return NullValue.Instance;
    };

    var exception = Assert.Throws<RuntimeException>(() => runtime.RunUntilComplete(runtime.RunFromFile("main.alk")));

    Assert.Contains("sealed", exception.Message);
    Assert.Empty(logged);
  }

  // ── Helpers ───────────────────────────────────────────────────────────────

  /// <summary>
  /// A minimal <see cref="IAsyncOperationBinder"/> backed by a lambda, so
  /// end-to-end tests can inject simple, synchronously-completing operations
  /// without standing up a full host binder implementation.
  /// </summary>
  private sealed class LambdaOperationBinder : IAsyncOperationBinder
  {
    private readonly Func<PendingOperation, Task<ALKScriptValue>> _start;

    public LambdaOperationBinder(Func<PendingOperation, Task<ALKScriptValue>> start)
    {
      _start = start;
    }

    public Task<ALKScriptValue> Start(PendingOperation operation) => _start(operation);

    public void Discard(PendingOperation operation, Action<Exception> onFault) { }

    public void OnOperationFaulted(PendingOperation operation, Exception fault) { }
  }
}
