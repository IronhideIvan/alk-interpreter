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
    private static readonly IReadOnlyDictionary<string, string> EmptyResolutions =
      new Dictionary<string, string>();

    /// <summary>
    /// The module's identity within the graph: a fully-qualified, normalized
    /// file path for <see cref="ModuleKind.File"/> modules, or the bare
    /// specifier (e.g. "collections") for <see cref="ModuleKind.Core"/> modules.
    /// </summary>
    public string Identifier { get; }

    public ModuleKind Kind { get; }

    public ProgramNode Program { get; }

    /// <summary>
    /// Maps each raw import specifier that appears in this module's source
    /// (e.g. <c>"./animals"</c> or <c>"console"</c>) to the resolved module
    /// identifier used as the key in <see cref="ModuleGraph.Modules"/>
    /// (e.g. <c>"animals.alk"</c> or <c>"console"</c>). Populated by the
    /// loader as it resolves imports; empty for modules with no imports.
    /// </summary>
    public IReadOnlyDictionary<string, string> ImportResolutions { get; }

    public LoadedModule(string identifier, ModuleKind kind, ProgramNode program,
      IReadOnlyDictionary<string, string>? importResolutions = null)
    {
      Identifier = identifier;
      Kind = kind;
      Program = program;
      ImportResolutions = importResolutions ?? EmptyResolutions;
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

    /// <summary>
    /// "True global" prelude programs — ordinary top-level declarations that
    /// the runtime wants seeded into the root environment, in order, before
    /// the entry module's own declarations run (see <c>IGlobalPreludeProvider</c>
    /// and <c>ProgramEvaluator</c>'s evaluation order). Compiled once, here at
    /// load time, so the evaluator never needs a lexer/parser of its own.
    /// Empty when the loader was given no prelude provider.
    /// </summary>
    public IReadOnlyList<ProgramNode> GlobalPreludes { get; }

    public ModuleGraph(LoadedModule entryModule, IReadOnlyDictionary<string, LoadedModule> modules)
      : this(entryModule, modules, System.Array.Empty<ProgramNode>())
    {
    }

    public ModuleGraph(LoadedModule entryModule, IReadOnlyDictionary<string, LoadedModule> modules, IReadOnlyList<ProgramNode> globalPreludes)
    {
      EntryModule = entryModule;
      Modules = modules;
      GlobalPreludes = globalPreludes;
    }
  }
}
