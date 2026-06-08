using ALKScript.Interpreter.Common;
using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Common.Modules;
using ALKScript.Interpreter.Common.Token;
using ALKScript.Interpreter.Lexer;
using ALKScript.Interpreter.Parser;
using ALKScript.Interpreter.Parser.Modules;

namespace Tests.ALKScript.Interpreter.Parser.Modules;

public class ProgramLoaderTests
{
  /// <summary>Wraps a real lexer and counts how many times each source string is tokenized, to verify memoization.</summary>
  private class CountingLexer : IScriptLexer
  {
    private readonly ALKScriptLexer _inner = new();

    public Dictionary<string, int> CallCountsBySource { get; } = new();

    public IEnumerable<ALKScriptToken> Tokenize(string contents)
    {
      CallCountsBySource[contents] = CallCountsBySource.GetValueOrDefault(contents) + 1;
      return _inner.Tokenize(contents);
    }
  }

  private static ProgramLoader CreateLoader(
    IReadOnlyDictionary<string, string> files,
    IReadOnlyDictionary<string, string>? coreModuleSources = null,
    IScriptLexer? lexer = null,
    IGlobalPreludeProvider? globalPreludes = null)
  {
    var fileReader = new FakeModuleFileReader(files);
    var coreModules = new FakeCoreModuleProvider(coreModuleSources ?? new Dictionary<string, string>());

    return new ProgramLoader(lexer ?? new ALKScriptLexer(), new ALKScriptParser(), fileReader, coreModules, globalPreludes);
  }

  [Fact]
  public void Load_EntryWithNoImports_ProducesGraphWithOnlyTheEntryModule()
  {
    var files = new Dictionary<string, string>
    {
      ["main.alk"] = "var x = 1;",
    };

    ModuleGraph graph = CreateLoader(files).Load("main.alk");

    Assert.Equal("main.alk", graph.EntryModule.Identifier);
    Assert.Equal(ModuleKind.File, graph.EntryModule.Kind);
    Assert.Single(graph.Modules);
    Assert.Same(graph.EntryModule, graph.Modules["main.alk"]);
  }

  [Fact]
  public void Load_MissingEntryFile_ThrowsFileNotFoundException()
  {
    var loader = CreateLoader(new Dictionary<string, string>());

    Assert.Throws<FileNotFoundException>(() => loader.Load("main.alk"));
  }

  [Fact]
  public void Load_RelativeImportWithoutExtension_ResolvesToAlkFileAndParsesIt()
  {
    var files = new Dictionary<string, string>
    {
      ["main.alk"] = "import { add } from \"./models\";\n\nvar sum = add(1, 2);",
      ["models.alk"] = "export function int add(int a, int b) { return a + b; }",
    };

    ModuleGraph graph = CreateLoader(files).Load("main.alk");

    Assert.Equal(2, graph.Modules.Count);

    LoadedModule target = graph.Modules["models.alk"];
    Assert.Equal(ModuleKind.File, target.Kind);
    Assert.Single(target.Program.Declarations);
  }

  [Fact]
  public void Load_NamespaceImport_DoesNotRequireExportedNameValidation()
  {
    var files = new Dictionary<string, string>
    {
      ["main.alk"] = "import * as Models from \"./models\";",
      ["models.alk"] = "export class Person {}",
    };

    ModuleGraph graph = CreateLoader(files).Load("main.alk");

    Assert.Equal(2, graph.Modules.Count);
  }

  [Fact]
  public void Load_DiamondImport_LoadsSharedModuleOnceAndMemoizesIt()
  {
    var sharedSource = "export class Shared {}";

    var files = new Dictionary<string, string>
    {
      ["main.alk"] = "import { A } from \"./a\";\nimport { B } from \"./b\";",
      ["a.alk"] = "import { Shared } from \"./shared\";\nexport class A {}",
      ["b.alk"] = "import { Shared } from \"./shared\";\nexport class B {}",
      ["shared.alk"] = sharedSource,
    };

    var lexer = new CountingLexer();
    ModuleGraph graph = CreateLoader(files, lexer: lexer).Load("main.alk");

    Assert.Equal(4, graph.Modules.Count);
    Assert.Single(graph.Modules["shared.alk"].Program.Declarations);

    // Reaching "shared.alk" through both "a.alk" and "b.alk" must lex/parse it
    // only once rather than re-loading it for each importer.
    Assert.Equal(1, lexer.CallCountsBySource[sharedSource]);
  }

  [Fact]
  public void Load_UnresolvableRelativeImport_ThrowsModuleLoadException()
  {
    var files = new Dictionary<string, string>
    {
      ["main.alk"] = "import { Thing } from \"./missing\";",
    };

    var exception = Assert.Throws<ModuleLoadException>(() => CreateLoader(files).Load("main.alk"));
    Assert.Contains("Cannot find module", exception.Message);
  }

  [Fact]
  public void Load_CircularImport_ThrowsModuleLoadException()
  {
    var files = new Dictionary<string, string>
    {
      ["main.alk"] = "import { X } from \"./x\";",
      ["x.alk"] = "import { Y } from \"./y\";\nexport class X {}",
      ["y.alk"] = "import { X } from \"./x\";\nexport class Y {}",
    };

    var exception = Assert.Throws<ModuleLoadException>(() => CreateLoader(files).Load("main.alk"));
    Assert.Contains("Circular import", exception.Message);
  }

  [Fact]
  public void Load_NamedImportOfUnexportedDeclaration_ThrowsModuleLoadException()
  {
    var files = new Dictionary<string, string>
    {
      ["main.alk"] = "import { helper } from \"./models\";",
      ["models.alk"] = "function int helper(int a) { return a; }",
    };

    var exception = Assert.Throws<ModuleLoadException>(() => CreateLoader(files).Load("main.alk"));
    Assert.Contains("has no exported member", exception.Message);
    Assert.Contains("helper", exception.Message);
  }

  [Fact]
  public void Load_RelativeImportFromNestedDirectory_ResolvesAgainstImportingFilesDirectory()
  {
    var files = new Dictionary<string, string>
    {
      ["main.alk"] = "import { Util } from \"./lib/util\";",
      ["lib/util.alk"] = "import { Shared } from \"../shared\";\nexport class Util {}",
      ["shared.alk"] = "export class Shared {}",
    };

    ModuleGraph graph = CreateLoader(files).Load("main.alk");

    Assert.Equal(3, graph.Modules.Count);
    Assert.Contains("lib/util.alk", graph.Modules.Keys);
    Assert.Contains("shared.alk", graph.Modules.Keys);
  }

  [Fact]
  public void Load_KnownCoreModuleImport_ResolvesThroughProvider()
  {
    var files = new Dictionary<string, string>
    {
      ["main.alk"] = "import { HttpClient } from \"http\";",
    };

    var coreModules = new Dictionary<string, string>
    {
      ["http"] = "export class HttpClient {}",
    };

    ModuleGraph graph = CreateLoader(files, coreModules).Load("main.alk");

    Assert.Equal(2, graph.Modules.Count);

    LoadedModule httpModule = graph.Modules["http"];
    Assert.Equal(ModuleKind.Core, httpModule.Kind);
    Assert.Single(httpModule.Program.Declarations);
  }

  [Fact]
  public void Load_UnknownCoreModuleImport_ThrowsModuleLoadException()
  {
    var files = new Dictionary<string, string>
    {
      ["main.alk"] = "import { Thing } from \"nope\";",
    };

    var exception = Assert.Throws<ModuleLoadException>(() => CreateLoader(files).Load("main.alk"));
    Assert.Contains("Cannot find core module", exception.Message);
  }

  [Fact]
  public void Load_NamedImportOfUnexportedNameFromCoreModule_ThrowsModuleLoadException()
  {
    var files = new Dictionary<string, string>
    {
      ["main.alk"] = "import { Missing } from \"http\";",
    };

    var coreModules = new Dictionary<string, string>
    {
      ["http"] = "export class HttpClient {}",
    };

    var exception = Assert.Throws<ModuleLoadException>(() => CreateLoader(files, coreModules).Load("main.alk"));
    Assert.Contains("Missing", exception.Message);
  }

  [Fact]
  public void Load_WithUnnamedPreludeSources_PopulatesGlobalPreludesInOrderAndExcludesThemFromTheGraph()
  {
    var files = new Dictionary<string, string> { ["main.alk"] = "var x = 1;" };

    var preludes = new FakeGlobalPreludeProvider(
      GlobalPreludeSource.Global("native function void print(Object value);"),
      GlobalPreludeSource.Global("function void assert(bool condition) {}"));

    ModuleGraph graph = CreateLoader(files, globalPreludes: preludes).Load("main.alk");

    Assert.Equal(2, graph.GlobalPreludes.Count);
    Assert.Single(graph.GlobalPreludes[0].Declarations);
    Assert.Single(graph.GlobalPreludes[1].Declarations);

    // Unnamed prelude sources have no module identity — they seed the root
    // environment directly and never appear in the module graph.
    Assert.Single(graph.Modules);
  }

  [Fact]
  public void Load_NamedPreludeSource_IsImportableAsACoreModuleAndAbsentFromGlobalPreludes()
  {
    var files = new Dictionary<string, string>
    {
      ["main.alk"] = "import { HttpClient } from \"http\";",
    };

    var preludes = new FakeGlobalPreludeProvider(
      GlobalPreludeSource.Module("http", "export class HttpClient {}"));

    ModuleGraph graph = CreateLoader(files, globalPreludes: preludes).Load("main.alk");

    Assert.Empty(graph.GlobalPreludes);

    LoadedModule httpModule = graph.Modules["http"];
    Assert.Equal(ModuleKind.Core, httpModule.Kind);
    Assert.Single(httpModule.Program.Declarations);
  }

  [Fact]
  public void Load_NamedPreludeSource_TakesPrecedenceOverACoreModuleProviderForTheSameSpecifier()
  {
    var files = new Dictionary<string, string>
    {
      ["main.alk"] = "import { FromPrelude } from \"http\";",
    };

    var coreModules = new Dictionary<string, string> { ["http"] = "export class FromProvider {}" };
    var preludes = new FakeGlobalPreludeProvider(
      GlobalPreludeSource.Module("http", "export class FromPrelude {}"));

    ModuleGraph graph = CreateLoader(files, coreModules, globalPreludes: preludes).Load("main.alk");

    LoadedModule httpModule = graph.Modules["http"];
    Assert.Single(httpModule.Program.Declarations);
    Assert.Contains(httpModule.Program.Declarations, decl =>
      decl is ExportDecl exportDecl && exportDecl.Declaration is ClassDecl classDecl && classDecl.Name.Lexeme == "FromPrelude");
  }

  [Fact]
  public void Load_NamedImportOfUnexportedNameFromNamedPreludeModule_ThrowsModuleLoadException()
  {
    var files = new Dictionary<string, string>
    {
      ["main.alk"] = "import { Missing } from \"http\";",
    };

    var preludes = new FakeGlobalPreludeProvider(
      GlobalPreludeSource.Module("http", "export class HttpClient {}"));

    var exception = Assert.Throws<ModuleLoadException>(() => CreateLoader(files, globalPreludes: preludes).Load("main.alk"));
    Assert.Contains("has no exported member", exception.Message);
    Assert.Contains("Missing", exception.Message);
  }
}
