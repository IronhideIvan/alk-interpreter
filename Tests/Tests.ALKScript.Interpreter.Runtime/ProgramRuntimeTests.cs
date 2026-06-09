using System.Collections.Generic;
using System.IO;
using ALKScript.Interpreter.Common.Evaluation.Scheduling;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Parser.Modules;
using Tests.ALKScript.Interpreter.Runtime.Support;

namespace Tests.ALKScript.Interpreter.Runtime;

/// <summary>
/// Integration tests for <see cref="ALKScript.Interpreter.Runtime.ProgramRuntime"/>.
/// Each test exercises the full lex → load → evaluate pipeline through the two
/// public entry points: <c>RunFromSource</c> and <c>RunFromFile</c>.
/// </summary>
public class ProgramRuntimeTests : RuntimeTestBase
{
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

  [Fact(Skip = "Import declarations are not yet evaluated — StatementExecutor has no ImportDecl case")]
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

  [Fact(Skip = "Import declarations are not yet evaluated — StatementExecutor has no ImportDecl case")]
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
  public void RunFromSource_EvaluationIsCompleted_AfterRunUntilComplete()
  {
    var (runtime, scheduler) = CreateRuntimeForEvaluation();

    ScriptEvaluation evaluation = runtime.RunFromSource($"{RecordDeclaration}var x = 1;");
    scheduler.RunUntilComplete(evaluation);

    Assert.True(evaluation.IsCompleted);
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

  [Fact(Skip = "Import declarations are not yet evaluated — StatementExecutor has no ImportDecl case")]
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
}
