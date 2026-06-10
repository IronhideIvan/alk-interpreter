using System.Collections.Generic;
using System.IO;
using System.Linq;
using ALKScript.Interpreter.Common;
using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Common.Modules;

namespace ALKScript.Interpreter.Parser.Modules
{
  /// <summary>
  /// Assembles the full module graph reachable from an entry file: parses the
  /// entry module, then walks its <see cref="ImportDecl"/>s — resolving each
  /// specifier per §9.2 of the language spec, recursively lexing and parsing
  /// any not-yet-loaded file modules, and recording core-module specifiers as
  /// opaque references for the interpreter's standard-library layer to
  /// resolve. Already-loaded modules are memoized by resolved identifier so a
  /// module imported from multiple places is only read and parsed once.
  ///
  /// This is the layer above <see cref="IScriptLexer"/>/<see cref="IScriptParser"/>
  /// that owns filesystem resolution and whole-program bookkeeping — concerns
  /// the parser deliberately does not have, since it operates purely on token
  /// streams.
  /// </summary>
  public class ProgramLoader : IProgramLoader
  {
    private const string ModuleFileExtension = ".alk";

    private readonly IScriptLexer _lexer;
    private readonly IScriptParser _parser;
    private readonly IModuleFileReader _fileReader;
    private readonly ICoreModuleProvider _coreModules;
    private readonly IGlobalPreludeProvider? _globalPreludes;

    /// <summary>
    /// Named (<see cref="GlobalPreludeSource.Module"/>) prelude sources,
    /// compiled and keyed by their module specifier — populated at the start
    /// of each <see cref="Load"/> and consulted by <see cref="ResolveCoreModule"/>
    /// alongside <see cref="_coreModules"/>, so a runtime can describe an
    /// importable core module as raw source text without standing up a whole
    /// <see cref="ICoreModuleProvider"/>.
    /// </summary>
    private IReadOnlyDictionary<string, ProgramNode> _namedPreludeModules = EmptyNamedPreludeModules;

    private static readonly IReadOnlyDictionary<string, ProgramNode> EmptyNamedPreludeModules = new Dictionary<string, ProgramNode>();

    public ProgramLoader(IScriptLexer lexer, IScriptParser parser, IModuleFileReader fileReader, ICoreModuleProvider coreModules, IGlobalPreludeProvider? globalPreludes = null)
    {
      _lexer = lexer;
      _parser = parser;
      _fileReader = fileReader;
      _coreModules = coreModules;
      _globalPreludes = globalPreludes;
    }

    /// <summary>
    /// Loads the module graph rooted at <paramref name="entryFilePath"/>.
    /// Throws <see cref="FileNotFoundException"/> if the entry file itself
    /// does not exist, or <see cref="ModuleLoadException"/> if an import
    /// within the graph cannot be resolved, forms a cycle, or names a
    /// declaration its target module does not export.
    /// </summary>
    public ModuleGraph Load(string entryFilePath)
    {
      if (!_fileReader.FileExists(entryFilePath))
      {
        throw new FileNotFoundException($"Cannot find entry module '{entryFilePath}'.");
      }

      // Compile the injected prelude before walking the entry module's
      // imports — named sources need to already be in _namedPreludeModules
      // for ResolveCoreModule to find them as the graph is assembled.
      IReadOnlyList<ProgramNode> globalPreludes = CompilePreludes();

      var modules = new Dictionary<string, LoadedModule>();
      var loadingStack = new HashSet<string>();

      LoadedModule entry = LoadFileModule(entryFilePath, modules, loadingStack);

      return new ModuleGraph(entry, modules, globalPreludes);
    }

    /// <summary>
    /// Parses <paramref name="source"/> as the entry module, applies the same
    /// global prelude and core-module resolution as <see cref="Load"/>, and
    /// returns the assembled graph. Relative-path imports are rejected because
    /// there is no base directory to resolve against.
    /// </summary>
    public ModuleGraph LoadFromSource(string source)
    {
      IReadOnlyList<ProgramNode> globalPreludes = CompilePreludes();

      var modules = new Dictionary<string, LoadedModule>();

      ProgramNode program = _parser.ParseTokens(_lexer.Tokenize(source));

      var importResolutions = new Dictionary<string, string>();
      var entry = new LoadedModule("<source>", ModuleKind.File, program, importResolutions);

      foreach (ImportDecl import in program.Imports)
      {
        string specifier = import.Source.Lexeme;

        if (IsRelativeFilePathSpecifier(specifier))
        {
          throw new ModuleLoadException(import.Source, "Relative-path imports are not supported when running from source.");
        }

        ResolveCoreModule(import, specifier, modules);
        importResolutions[specifier] = specifier; // core modules: identifier == specifier
      }

      return new ModuleGraph(entry, modules, globalPreludes);
    }

    /// <summary>
    /// Compiles every source supplied by the injected <see cref="IGlobalPreludeProvider"/>
    /// through the same lex -&gt; parse pipeline used for file and core modules
    /// — keeping that mechanism centralized here rather than duplicated in (or
    /// requiring a lexer/parser injection into) the evaluator — and splits the
    /// results by <see cref="GlobalPreludeSource.ModuleName"/>: unnamed sources
    /// become the returned "true global" list (in order), while named ones are
    /// stashed in <see cref="_namedPreludeModules"/> for <see cref="ResolveCoreModule"/>
    /// to fold into the graph as importable core modules. Returns an empty list,
    /// and leaves <see cref="_namedPreludeModules"/> empty, when no provider was supplied.
    /// </summary>
    private IReadOnlyList<ProgramNode> CompilePreludes()
    {
      if (_globalPreludes == null || _globalPreludes.Sources.Count == 0)
      {
        _namedPreludeModules = EmptyNamedPreludeModules;
        return System.Array.Empty<ProgramNode>();
      }

      var globals = new List<ProgramNode>();
      var named = new Dictionary<string, ProgramNode>();

      foreach (GlobalPreludeSource entry in _globalPreludes.Sources)
      {
        ProgramNode program = _parser.ParseTokens(_lexer.Tokenize(entry.Source));

        if (entry.ModuleName == null)
        {
          globals.Add(program);
        }
        else
        {
          named[entry.ModuleName] = program;
        }
      }

      _namedPreludeModules = named;
      return globals;
    }

    private LoadedModule LoadFileModule(string path, Dictionary<string, LoadedModule> modules, HashSet<string> loadingStack)
    {
      if (modules.TryGetValue(path, out LoadedModule? cached))
      {
        return cached;
      }

      string source = _fileReader.ReadFile(path);
      ProgramNode program = _parser.ParseTokens(_lexer.Tokenize(source));

      // Memoize before walking imports so that diamond-shaped graphs resolve
      // the shared module to the same instance, and so the loading stack
      // below can detect cycles that pass back through this module.
      var importResolutions = new Dictionary<string, string>();
      var module = new LoadedModule(path, ModuleKind.File, program, importResolutions);

      modules[path] = module;
      loadingStack.Add(path);

      try
      {
        foreach (ImportDecl import in program.Imports)
        {
          ResolveImport(path, import, modules, loadingStack, importResolutions);
        }

        foreach (Stmt declaration in program.Declarations)
        {
          if (declaration is ReExportDecl reExport)
          {
            ResolveReExport(path, reExport, modules, loadingStack, importResolutions);
          }
        }
      }
      finally
      {
        loadingStack.Remove(path);
      }

      return module;
    }

    private void ResolveImport(string importingFilePath, ImportDecl import, Dictionary<string, LoadedModule> modules, HashSet<string> loadingStack, Dictionary<string, string> importResolutions)
    {
      string specifier = import.Source.Lexeme;

      if (!IsRelativeFilePathSpecifier(specifier))
      {
        ResolveCoreModule(import, specifier, modules);
        importResolutions[specifier] = specifier; // core modules: identifier == specifier
        return;
      }

      string resolvedPath = ResolveFilePathSpecifier(importingFilePath, specifier);

      if (resolvedPath == null!)
      {
        throw new ModuleLoadException(import.Source, $"Cannot find module '{specifier}'.");
      }

      if (loadingStack.Contains(resolvedPath))
      {
        throw new ModuleLoadException(import.Source, $"Circular import detected while loading module '{specifier}'.");
      }

      LoadedModule target = LoadFileModule(resolvedPath, modules, loadingStack);
      importResolutions[specifier] = resolvedPath;

      ValidateNamedImports(import, specifier, target.Program);
    }

    /// <summary>
    /// Resolves the "from" target of a re-export declaration the same way an
    /// <see cref="ImportDecl"/> is resolved — recursively loading file modules,
    /// recording an entry in <paramref name="importResolutions"/>, detecting
    /// cycles, and validating that the target module actually exports every
    /// re-exported name.
    /// </summary>
    private void ResolveReExport(string importingFilePath, ReExportDecl reExport, Dictionary<string, LoadedModule> modules, HashSet<string> loadingStack, Dictionary<string, string> importResolutions)
    {
      string specifier = reExport.Source.Lexeme;

      if (!IsRelativeFilePathSpecifier(specifier))
      {
        ResolveCoreModuleForReExport(reExport, specifier, modules);
        importResolutions[specifier] = specifier; // core modules: identifier == specifier
        return;
      }

      string resolvedPath = ResolveFilePathSpecifier(importingFilePath, specifier);

      if (resolvedPath == null!)
      {
        throw new ModuleLoadException(reExport.Source, $"Cannot find module '{specifier}'.");
      }

      if (loadingStack.Contains(resolvedPath))
      {
        throw new ModuleLoadException(reExport.Source, $"Circular import detected while loading module '{specifier}'.");
      }

      LoadedModule target = LoadFileModule(resolvedPath, modules, loadingStack);
      importResolutions[specifier] = resolvedPath;

      ValidateSpecifiersExported(reExport.Specifiers, specifier, target.Program);
    }

    private void ResolveCoreModuleForReExport(ReExportDecl reExport, string specifier, Dictionary<string, LoadedModule> modules)
    {
      if (!modules.TryGetValue(specifier, out LoadedModule? target))
      {
        ProgramNode? program = ResolveCoreModuleProgram(specifier);

        if (program == null)
        {
          throw new ModuleLoadException(reExport.Source, $"Cannot find core module '{specifier}'.");
        }

        target = new LoadedModule(specifier, ModuleKind.Core, program);
        modules[specifier] = target;
      }

      ValidateSpecifiersExported(reExport.Specifiers, specifier, target.Program);
    }

    private void ResolveCoreModule(ImportDecl import, string specifier, Dictionary<string, LoadedModule> modules)
    {
      if (!modules.TryGetValue(specifier, out LoadedModule? target))
      {
        ProgramNode? program = ResolveCoreModuleProgram(specifier);

        if (program == null)
        {
          throw new ModuleLoadException(import.Source, $"Cannot find core module '{specifier}'.");
        }

        target = new LoadedModule(specifier, ModuleKind.Core, program);
        modules[specifier] = target;
      }

      ValidateNamedImports(import, specifier, target.Program);
    }

    /// <summary>
    /// Resolves a core-module specifier's definition, checking named
    /// (<see cref="GlobalPreludeSource.Module"/>) prelude sources first and
    /// falling back to the injected <see cref="ICoreModuleProvider"/> — so a
    /// runtime can describe a core module either way, and the prelude can
    /// supply one the provider doesn't know about (or vice versa). Returns
    /// <c>null</c> when neither knows the specifier.
    /// </summary>
    private ProgramNode? ResolveCoreModuleProgram(string specifier)
    {
      if (_namedPreludeModules.TryGetValue(specifier, out ProgramNode? fromPrelude))
      {
        return fromPrelude;
      }

      if (_coreModules.AvailableModules.Contains(specifier))
      {
        return _coreModules.GetModule(specifier);
      }

      return null;
    }

    private static bool IsRelativeFilePathSpecifier(string specifier)
    {
      return specifier.StartsWith("./") || specifier.StartsWith("../");
    }

    /// <summary>
    /// Resolves a relative-path specifier against the importing file's
    /// directory, trying the path as given and then with the ".alk"
    /// extension appended (specifiers omit it, e.g. "./models" → "models.alk").
    /// Returns null when neither candidate exists.
    /// </summary>
    private string ResolveFilePathSpecifier(string importingFilePath, string specifier)
    {
      string directory = _fileReader.GetDirectoryName(importingFilePath);
      string candidate = _fileReader.CombinePath(directory, specifier);

      if (_fileReader.FileExists(candidate))
      {
        return candidate;
      }

      string withExtension = candidate + ModuleFileExtension;

      if (_fileReader.FileExists(withExtension))
      {
        return withExtension;
      }

      return null!;
    }

    /// <summary>
    /// Validates that every name in a named-imports clause is actually
    /// exported by the target module (§9.2: "Importing a name the target
    /// module does not export ... is a compile-time error"). Namespace
    /// imports bring in whatever the module exports, so they need no
    /// per-name check.
    /// </summary>
    private static void ValidateNamedImports(ImportDecl import, string specifier, ProgramNode targetProgram)
    {
      if (!(import.Clause is NamedImportsClause namedImports))
      {
        return;
      }

      ValidateSpecifiersExported(namedImports.Specifiers, specifier, targetProgram);
    }

    /// <summary>
    /// Validates that every name in <paramref name="specifiers"/> is actually
    /// exported by <paramref name="targetProgram"/>, used for both named
    /// imports and re-export "from" clauses.
    /// </summary>
    private static void ValidateSpecifiersExported(IReadOnlyList<ImportSpecifier> specifiers, string specifier, ProgramNode targetProgram)
    {
      HashSet<string> exportedNames = CollectExportedNames(targetProgram);

      foreach (ImportSpecifier importSpecifier in specifiers)
      {
        string name = importSpecifier.Name.Lexeme;

        if (!exportedNames.Contains(name))
        {
          throw new ModuleLoadException(importSpecifier.Name, $"Module '{specifier}' has no exported member '{name}'.");
        }
      }
    }

    private static HashSet<string> CollectExportedNames(ProgramNode program)
    {
      var names = new HashSet<string>();

      foreach (Stmt declaration in program.Declarations)
      {
        if (declaration is ExportDecl exportDecl)
        {
          string? name = GetDeclarationName(exportDecl.Declaration);

          if (name != null)
          {
            names.Add(name);
          }
        }
        else if (declaration is ReExportDecl reExportDecl)
        {
          foreach (ImportSpecifier specifier in reExportDecl.Specifiers)
          {
            names.Add(specifier.Alias?.Lexeme ?? specifier.Name.Lexeme);
          }
        }
      }

      return names;
    }

    private static string? GetDeclarationName(Decl declaration)
    {
      switch (declaration)
      {
        case ClassDecl classDecl:
          return classDecl.Name.Lexeme;
        case FunctionDecl functionDecl:
          return functionDecl.Name.Lexeme;
        case VariableDecl variableDecl:
          return variableDecl.Name.Lexeme;
        default:
          return null;
      }
    }
  }
}
