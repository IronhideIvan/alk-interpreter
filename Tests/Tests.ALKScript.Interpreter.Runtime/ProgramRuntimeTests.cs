using System.Collections.Generic;
using System.IO;
using ALKScript.Interpreter.Common.Evaluation.Scheduling;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Common.Modules;
using ALKScript.Interpreter.Evaluator.Cursor;
using ALKScript.Interpreter.Parser.Modules;
using Tests.ALKScript.Interpreter.Runtime.Support;

namespace Tests.ALKScript.Interpreter.Runtime;

/// <summary>
/// Integration tests for <see cref="ALKScript.Interpreter.Runtime.ProgramRuntime"/>.
/// Each test exercises the full lex → load → evaluate pipeline through the
/// public entry points.
/// </summary>
public class ProgramRuntimeTests : RuntimeTestBase
{
  // ── LoadFromSource ───────────────────────────────────────────────────────

  [Fact]
  public void LoadFromSource_ReturnsModuleGraph_WithoutExecuting()
  {
    var runtime = CreateRuntimeForEvaluation();
    var recorded = new List<ALKScriptValue>();
    runtime.NativeBindings["record"] = args => { recorded.Add(args[0]); return NullValue.Instance; };

    ModuleGraph graph = runtime.LoadFromSource($"{RecordDeclaration}record(1);");

    Assert.NotNull(graph);
    Assert.NotNull(graph.EntryModule);
    Assert.Empty(recorded); // no execution yet
  }

  [Fact]
  public void LoadFromSource_WithRelativePathImport_ThrowsModuleLoadException()
  {
    var runtime = CreateRuntimeForEvaluation();

    Assert.Throws<ModuleLoadException>(() =>
      runtime.LoadFromSource("import { helper } from \"./helpers\";"));
  }

  // ── LoadFromFile ─────────────────────────────────────────────────────────

  [Fact]
  public void LoadFromFile_ReturnsModuleGraph_WithoutExecuting()
  {
    var runtime = CreateRuntimeForEvaluation(
      files: new Dictionary<string, string>
      {
        ["main.alk"] = $"{RecordDeclaration}record(99);"
      });
    var recorded = new List<ALKScriptValue>();
    runtime.NativeBindings["record"] = args => { recorded.Add(args[0]); return NullValue.Instance; };

    ModuleGraph graph = runtime.LoadFromFile("main.alk");

    Assert.NotNull(graph);
    Assert.NotNull(graph.EntryModule);
    Assert.Empty(recorded); // no execution yet
  }

  [Fact]
  public void LoadFromFile_NonexistentFile_ThrowsFileNotFoundException()
  {
    var runtime = CreateRuntimeForEvaluation();

    Assert.Throws<FileNotFoundException>(() =>
      runtime.LoadFromFile("missing.alk"));
  }

  // ── RunFromGraph ─────────────────────────────────────────────────────────

  [Fact]
  public void RunFromGraph_ExecutesStatements()
  {
    var runtime = CreateRuntimeForEvaluation();
    var recorded = new List<ALKScriptValue>();
    runtime.NativeBindings["record"] = args => { recorded.Add(args[0]); return NullValue.Instance; };

    ModuleGraph graph = runtime.LoadFromSource($"{RecordDeclaration}record(7);");
    runtime.RunFromGraph(graph).RunToCompletion();

    var value = Assert.IsType<IntValue>(Assert.Single(recorded));
    Assert.Equal(7L, value.Value);
  }

  [Fact]
  public void RunFromGraph_EvaluationIsCompleted_AfterRunToCompletion()
  {
    var runtime = CreateRuntimeForEvaluation();

    ModuleGraph graph = runtime.LoadFromSource($"{RecordDeclaration}var x = 1;");
    var run = runtime.RunFromGraph(graph);
    run.RunToCompletion();

    Assert.Equal(ProgramRunResult.Completed, run.Result);
  }

  [Fact]
  public void RunFromGraph_SameGraph_ProducesIndependentEvaluations()
  {
    // The same compiled graph can be run multiple times; each call produces a
    // fully independent evaluation with its own state.
    var runtime = CreateRuntimeForEvaluation();
    var recorded = new List<ALKScriptValue>();
    runtime.NativeBindings["record"] = args => { recorded.Add(args[0]); return NullValue.Instance; };

    ModuleGraph graph = runtime.LoadFromSource($"{RecordDeclaration}record(5);");

    runtime.RunFromGraph(graph).RunToCompletion();
    runtime.RunFromGraph(graph).RunToCompletion();

    Assert.Equal(2, recorded.Count);
    Assert.All(recorded, v => Assert.Equal(5L, Assert.IsType<IntValue>(v).Value));
  }

  // ── RunFromSource ────────────────────────────────────────────────────────

  [Fact]
  public void RunFromSource_SimpleScript_ExecutesStatements()
  {
    var recorded = RunFromSource($"{RecordDeclaration}var x = 1 + 2;\nrecord(x);");

    var value = Assert.IsType<IntValue>(Assert.Single(recorded));
    Assert.Equal(3L, value.Value);
  }

  [Fact]
  public void RunFromSource_WithGlobalPrelude_PreludeSymbolsAreVisibleWithoutImport()
  {
    // The prelude declares "greet" as a native — the entry source calls it with
    // no import or re-declaration, proving the prelude was seeded into the root
    // environment before the entry module ran.
    var calls = new List<ALKScriptValue>();

    RunFromSource(
      source: "greet(\"world\");",
      extraBindings: new ScriptNativeBindings
      {
        ["greet"] = args => { calls.Add(args[0]); return NullValue.Instance; }
      },
      preludes: new FakeGlobalPreludeProvider(
        GlobalPreludeSource.Global("native function void greet(string name);\n")));

    var value = Assert.IsType<StringValue>(Assert.Single(calls));
    Assert.Equal("world", value.Value);
  }

  [Fact]
  public void RunFromSource_WithCoreModuleImport_ImportsResolveFromProvider()
  {
    // "math" is supplied by the ICoreModuleProvider; the entry source imports
    // and calls its exported "double" function.
    var recorded = RunFromSource(
      source: $"import {{ double }} from \"math\";\n{RecordDeclaration}record(double(7));",
      coreModules: new Dictionary<string, string>
      {
        ["math"] = "export function int double(int n) { return n + n; }"
      });

    var value = Assert.IsType<IntValue>(Assert.Single(recorded));
    Assert.Equal(14L, value.Value);
  }

  [Fact]
  public void RunFromSource_WithPreludeNamedModule_IsImportableAsCoreModule()
  {
    // A GlobalPreludeSource.Module() entry is importable as a core module just
    // like one supplied by ICoreModuleProvider — it does NOT land in the global
    // scope unless imported.
    var recorded = RunFromSource(
      source: $"import {{ triple }} from \"utils\";\n{RecordDeclaration}record(triple(5));",
      preludes: new FakeGlobalPreludeProvider(
        GlobalPreludeSource.Module("utils", "export function int triple(int n) { return n + n + n; }\n")));

    var value = Assert.IsType<IntValue>(Assert.Single(recorded));
    Assert.Equal(15L, value.Value);
  }

  [Fact]
  public void RunFromSource_WithRelativePathImport_ThrowsModuleLoadException()
  {
    // Relative imports cannot be resolved without a base directory; ProgramLoader
    // rejects them with a clear ModuleLoadException.
    Assert.Throws<ModuleLoadException>(() =>
      RunFromSource("import { helper } from \"./helpers\";"));
  }

  [Fact]
  public void RunFromSource_EvaluationIsCompleted_AfterRunToCompletion()
  {
    var runtime = CreateRuntimeForEvaluation();

    var run = runtime.RunFromSource($"{RecordDeclaration}var x = 1;");
    run.RunToCompletion();

    Assert.Equal(ProgramRunResult.Completed, run.Result);
  }

  // ── RunFromFile ──────────────────────────────────────────────────────────

  [Fact]
  public void RunFromFile_SimpleScript_ExecutesStatements()
  {
    var files = new Dictionary<string, string>
    {
      ["main.alk"] = $"{RecordDeclaration}record(42);"
    };

    var recorded = RunFromFile("main.alk", files);

    var value = Assert.IsType<IntValue>(Assert.Single(recorded));
    Assert.Equal(42L, value.Value);
  }

  [Fact]
  public void RunFromFile_MultiModuleProgram_ResolvesFileImports()
  {
    // The entry module imports an exported function from a sibling file; the
    // result proves both were loaded, linked, and executed correctly.
    var files = new Dictionary<string, string>
    {
      ["main.alk"] = $"import {{ add }} from \"./math\";\n{RecordDeclaration}record(add(3, 4));",
      ["math.alk"] = "export function int add(int a, int b) { return a + b; }"
    };

    var recorded = RunFromFile("main.alk", files);

    var value = Assert.IsType<IntValue>(Assert.Single(recorded));
    Assert.Equal(7L, value.Value);
  }

  [Fact]
  public void RunFromFile_NonexistentFile_ThrowsFileNotFoundException()
  {
    Assert.Throws<FileNotFoundException>(() =>
      RunFromFile("missing.alk", new Dictionary<string, string>()));
  }

  [Fact]
  public void RunFromFile_EvaluationIsCompleted_AfterRunToCompletion()
  {
    var files = new Dictionary<string, string>
    {
      ["main.alk"] = $"{RecordDeclaration}var x = 1;"
    };
    var runtime = CreateRuntimeForEvaluation(files: files);

    var run = runtime.RunFromFile("main.alk");
    run.RunToCompletion();

    Assert.Equal(ProgramRunResult.Completed, run.Result);
  }

  // ── Independent runs ──────────────────────────────────────────────────────

  [Fact]
  public void RunFromSource_TwoIndependentRuns_BothCompleteIndependently()
  {
    // Two independent scripts are each run via their own ProgramRun; running
    // one to completion does not affect the other, and each produces its own
    // output.
    var runtime = CreateRuntimeForEvaluation();
    var recordedA = new List<ALKScriptValue>();
    var recordedB = new List<ALKScriptValue>();
    runtime.NativeBindings["recordA"] = args => { recordedA.Add(args[0]); return NullValue.Instance; };
    runtime.NativeBindings["recordB"] = args => { recordedB.Add(args[0]); return NullValue.Instance; };

    var runA = runtime.RunFromSource(
      "native function void recordA(Object v);\nrecordA(1);");
    runA.RunToCompletion();

    var runB = runtime.RunFromSource(
      "native function void recordB(Object v);\nrecordB(2);");
    runB.RunToCompletion();

    Assert.Equal(ProgramRunResult.Completed, runA.Result);
    Assert.Equal(ProgramRunResult.Completed, runB.Result);
    Assert.Equal(1L, Assert.IsType<IntValue>(Assert.Single(recordedA)).Value);
    Assert.Equal(2L, Assert.IsType<IntValue>(Assert.Single(recordedB)).Value);
  }

  [Fact]
  public void RunFromGraph_TwoIndependentRuns_BothCompleteIndependently()
  {
    // The same compiled graph can be run twice via independent ProgramRuns;
    // each produces its own output and completing one does not affect the
    // other.
    var runtime = CreateRuntimeForEvaluation();
    var recorded = new List<ALKScriptValue>();
    runtime.NativeBindings["record"] = args => { recorded.Add(args[0]); return NullValue.Instance; };

    ModuleGraph graph = runtime.LoadFromSource($"{RecordDeclaration}record(42);");
    var run1 = runtime.RunFromGraph(graph);
    var run2 = runtime.RunFromGraph(graph);

    run1.RunToCompletion();
    run2.RunToCompletion();

    Assert.Equal(ProgramRunResult.Completed, run1.Result);
    Assert.Equal(ProgramRunResult.Completed, run2.Result);
    Assert.Equal(2, recorded.Count);
    Assert.All(recorded, v => Assert.Equal(42L, Assert.IsType<IntValue>(v).Value));
  }
}
