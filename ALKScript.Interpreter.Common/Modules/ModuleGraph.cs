using System.Collections.Generic;
using ALKScript.Interpreter.Common.Ast;

namespace ALKScript.Interpreter.Common.Modules
{
  /// <summary>The two ways a module specifier can resolve, per §9.2 of the language spec.</summary>
  public enum ModuleKind
  {
    /// <summary>A relative-path specifier resolved to an ALKScript source file on disk.</summary>
    File,

    /// <summary>A bare specifier naming a standard-library core module (e.g. "collections").</summary>
    Core
  }

  /// <summary>
  /// A single module in the graph assembled by <see cref="IProgramLoader"/>,
  /// whether parsed from a source file or supplied by a core-module provider.
  /// </summary>
  public class LoadedModule
  {
    /// <summary>
    /// The module's identity within the graph: a fully-qualified, normalized
    /// file path for <see cref="ModuleKind.File"/> modules, or the bare
    /// specifier (e.g. "collections") for <see cref="ModuleKind.Core"/> modules.
    /// </summary>
    public string Identifier { get; }

    public ModuleKind Kind { get; }

    public ProgramNode Program { get; }

    public LoadedModule(string identifier, ModuleKind kind, ProgramNode program)
    {
      Identifier = identifier;
      Kind = kind;
      Program = program;
    }
  }

  /// <summary>
  /// The result of <see cref="IProgramLoader.Load"/>: the entry module plus
  /// every module reachable from it through "import" declarations, keyed by
  /// resolved identifier so the interpreter can look up a target module for
  /// each <see cref="ImportDecl"/> without re-resolving specifiers.
  /// </summary>
  public class ModuleGraph
  {
    public LoadedModule EntryModule { get; }
    public IReadOnlyDictionary<string, LoadedModule> Modules { get; }

    public ModuleGraph(LoadedModule entryModule, IReadOnlyDictionary<string, LoadedModule> modules)
    {
      EntryModule = entryModule;
      Modules = modules;
    }
  }
}
