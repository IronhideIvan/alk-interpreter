using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ALKScript.Interpreter.Common.Evaluation.Scheduling;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Evaluator.Cursor;
using ALKScript.Interpreter.Runtime;
using Tests.ALKScript.Interpreter.Runtime.Support;

namespace Tests.ALKScript.Interpreter.Runtime;

/// <summary>
/// End-to-end tests that run the same ALKScript programs as
/// <see cref="EndToEndTests"/> but using <c>new ProgramRuntime()</c> — the
/// zero-argument, zero-injection path a typical host uses. No fakes, no
/// custom loaders: the runtime reads files from the real filesystem, core
/// modules are registered via <see cref="ProgramRuntime.CoreModules"/>, and
/// native bindings are registered via <see cref="ProgramRuntime.NativeBindings"/>
/// and <see cref="ProgramRuntime.NativeMethodBindings"/>.
/// </summary>
public class IntegrationTests
{
  private static readonly string ScriptsDir = Path.Combine(
    Path.GetDirectoryName(typeof(IntegrationTests).Assembly.Location)!,
    "ALKScripts");

  private static string ScriptPath(string folder, string file) =>
    Path.Combine(ScriptsDir, folder, file);

  private static string ReadScript(string folder, string file) =>
    File.ReadAllText(ScriptPath(folder, file));

  // ── Animal Showcase ───────────────────────────────────────────────────────

  [Fact]
  public void AnimalShowcase_DefaultRuntime_ProducesExpectedOutput()
  {
    var logged = new List<string>();

    var runtime = new ProgramRuntime();
    runtime.CoreModules["console"] = ReadScript("AnimalShowcase", "console.alk");

    runtime.NativeBindings["log"] = args =>
    {
      logged.Add(((StringValue)args[0]).Value);
      return NullValue.Instance;
    };

    runtime.RunFromFile(ScriptPath("AnimalShowcase", "main.alk")).RunToCompletion(runtime.OperationBinder);

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
  public void ItemProcessor_DefaultRuntime_ProducesExpectedOutput()
  {
    var logged = new List<string>();

    var runtime = new ProgramRuntime();
    runtime.CoreModules["console"] = ReadScript("ItemProcessor", "console.alk");
    runtime.CoreModules["io"]      = ReadScript("ItemProcessor", "io.alk");

    runtime.NativeBindings["log"] = args =>
    {
      logged.Add(((StringValue)args[0]).Value);
      return NullValue.Instance;
    };

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

    runtime.OperationBinder = new LambdaOperationBinder(op =>
    {
      if (op.Name == "process")
      {
        var name = ((StringValue)op.Arguments[0]).Value;
        return Task.FromResult<ALKScriptValue>(new StringValue("[processed] " + name));
      }
      throw new InvalidOperationException($"Unknown async operation: '{op.Name}'.");
    });

    runtime.RunFromFile(ScriptPath("ItemProcessor", "main.alk")).RunToCompletion(runtime.OperationBinder);

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
  public void AsyncFetcher_DefaultRuntime_ProducesExpectedOutput()
  {
    var logged = new List<string>();

    var runtime = new ProgramRuntime();
    runtime.CoreModules["console"] = ReadScript("AsyncFetcher", "console.alk");
    runtime.CoreModules["network"] = ReadScript("AsyncFetcher", "network.alk");

    runtime.NativeBindings["log"] = args =>
    {
      logged.Add(((StringValue)args[0]).Value);
      return NullValue.Instance;
    };

    runtime.NativeMethodBindings["HttpClient", "get"] = (instance, args) =>
    {
      var url = ((StringValue)args[0]).Value;
      return new ThunkValue(Task.Run(async () =>
      {
        await Task.Delay(10);
        return (ALKScriptValue)new StringValue("[200] " + url);
      }));
    };

    // Each 'await client.get(url)' suspends with a genuinely pending Task
    // (Task.Run + Task.Delay); RunToCompletion waits on each one in turn and
    // resumes with its settled result, mirroring a real host driving the run.
    runtime.RunFromFile(ScriptPath("AsyncFetcher", "main.alk")).RunToCompletion(runtime.OperationBinder);

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
  public void PumpOrdering_DefaultRuntime_ScriptSuspendsAtEachAwait_LogsIncrementallyAcrossResumes()
  {
    var logged = new List<string>();
    var pendingReads = new List<TaskCompletionSource<ALKScriptValue>>();

    var runtime = new ProgramRuntime();
    runtime.CoreModules["console"] = ReadScript("PumpOrdering", "console.alk");
    runtime.CoreModules["sensor"]  = ReadScript("PumpOrdering", "sensor.alk");

    runtime.NativeBindings["log"] = args =>
    {
      logged.Add(((StringValue)args[0]).Value);
      return NullValue.Instance;
    };

    runtime.NativeMethodBindings["Sensor", "read"] = (instance, args) =>
    {
      var tcs = new TaskCompletionSource<ALKScriptValue>();
      pendingReads.Add(tcs);
      return new ThunkValue(tcs.Task);
    };

    var run = runtime.RunFromFile(ScriptPath("PumpOrdering", "main.alk"));

    Assert.Equal(new[] { "starting" }, logged);
    Assert.Equal(ProgramRunResult.Awaiting, run.Result);

    pendingReads[0].SetResult(new IntValue(42));
    run.Resume(run.PendingAwait!.Task!.GetAwaiter().GetResult());

    Assert.Equal(new[] { "starting", "reading-1: 42" }, logged);
    Assert.Equal(ProgramRunResult.Awaiting, run.Result);

    pendingReads[1].SetResult(new IntValue(99));
    run.Resume(run.PendingAwait!.Task!.GetAwaiter().GetResult());

    Assert.Equal(new[] { "starting", "reading-1: 42", "reading-2: 99", "done" }, logged);
    Assert.Equal(ProgramRunResult.Completed, run.Result);
  }

  // ── Custom IModuleFileReader ──────────────────────────────────────────────

  [Fact]
  public void CustomModuleFileReader_LoadsAndExecutesScript()
  {
    // Verifies the ProgramRuntime(IModuleFileReader) constructor: the runtime
    // uses the supplied reader for file-based imports while still wiring up
    // the default scheduler, evaluator, and CoreModules table.  FakeModuleFileReader
    // stands in for any host-supplied reader (asset bundle, network store, etc.).

    var logged = new List<string>();

    var reader = new FakeModuleFileReader(new Dictionary<string, string>
    {
      ["main.alk"]    = ReadScript("AnimalShowcase", "main.alk"),
      ["animals.alk"] = ReadScript("AnimalShowcase", "animals.alk"),
    });

    var runtime = new ProgramRuntime(reader);
    runtime.CoreModules["console"] = ReadScript("AnimalShowcase", "console.alk");

    runtime.NativeBindings["log"] = args =>
    {
      logged.Add(((StringValue)args[0]).Value);
      return NullValue.Instance;
    };

    runtime.RunFromFile("main.alk").RunToCompletion(runtime.OperationBinder);

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

  // ── Helpers ───────────────────────────────────────────────────────────────

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
