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

    public ProgramLoader(IScriptLexer lexer, IScriptParser parser, IModuleFileReader fileReader, ICoreModuleProvider coreModules)
    {
      _lexer = lexer;
      _parser = parser;
      _fileReader = fileReader;
      _coreModules = coreModules;
    }

    public ProgramLoader(IScriptLexer lexer, IScriptParser parser, ICoreModuleProvider coreModules)
      : this(lexer, parser, new FileSystemModuleFileReader(), coreModules)
    {
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

      var modules = new Dictionary<string, LoadedModule>();
      var loadingStack = new HashSet<string>();

      LoadedModule entry = LoadFileModule(entryFilePath, modules, loadingStack);

      return new ModuleGraph(entry, modules);
    }

    private LoadedModule LoadFileModule(string path, Dictionary<string, LoadedModule> modules, HashSet<string> loadingStack)
    {
      if (modules.TryGetValue(path, out LoadedModule? cached))
      {
        return cached;
      }

      string source = _fileReader.ReadFile(path);
      ProgramNode program = _parser.ParseTokens(_lexer.Tokenize(source));

      var module = new LoadedModule(path, ModuleKind.File, program);

      // Memoize before walking imports so that diamond-shaped graphs resolve
      // the shared module to the same instance, and so the loading stack
      // below can detect cycles that pass back through this module.
      modules[path] = module;
      loadingStack.Add(path);

      try
      {
        foreach (ImportDecl import in program.Imports)
        {
          ResolveImport(path, import, modules, loadingStack);
        }
      }
      finally
      {
        loadingStack.Remove(path);
      }

      return module;
    }

    private void ResolveImport(string importingFilePath, ImportDecl import, Dictionary<string, LoadedModule> modules, HashSet<string> loadingStack)
    {
      string specifier = import.Source.Lexeme;

      if (!IsRelativeFilePathSpecifier(specifier))
      {
        ResolveCoreModule(import, specifier, modules);
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

      ValidateNamedImports(import, specifier, target.Program);
    }

    private void ResolveCoreModule(ImportDecl import, string specifier, Dictionary<string, LoadedModule> modules)
    {
      if (!_coreModules.AvailableModules.Contains(specifier))
      {
        throw new ModuleLoadException(import.Source, $"Cannot find core module '{specifier}'.");
      }

      if (!modules.TryGetValue(specifier, out LoadedModule? target))
      {
        target = new LoadedModule(specifier, ModuleKind.Core, _coreModules.GetModule(specifier));
        modules[specifier] = target;
      }

      ValidateNamedImports(import, specifier, target.Program);
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

      HashSet<string> exportedNames = CollectExportedNames(targetProgram);

      foreach (ImportSpecifier importSpecifier in namedImports.Specifiers)
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
