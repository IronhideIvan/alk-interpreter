using System.Collections.Generic;
using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Common.Evaluation;
using ALKScript.Interpreter.Common.Evaluation.Scheduling;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Common.Modules;
using ALKScript.Interpreter.Evaluator.Cursor;
using ALKScript.Interpreter.Lexer;
using ALKScript.Interpreter.Parser;
using ALKScript.Interpreter.Parser.Modules;

namespace ALKScript.Interpreter.Runtime
{
  /// <summary>
  /// The primary host-facing entry point for running ALKScript programs.
  ///
  /// <para>
  /// Register host implementations for <c>native</c> function and method
  /// declarations via <see cref="NativeBindings"/> and
  /// <see cref="NativeMethodBindings"/> before running scripts.
  /// </para>
  ///
  /// <para>
  /// The zero-argument constructor is the intended path for most hosts: it
  /// creates the full default pipeline internally so the host needs nothing
  /// more than <c>new ProgramRuntime()</c>. The overloaded constructor accepts
  /// an explicit <see cref="IProgramLoader"/> for tests or advanced embeddings.
  /// </para>
  /// </summary>
  public class ProgramRuntime : IProgramRuntime
  {
    private readonly IProgramLoader _loader;

    /// <summary>
    /// ALKScript source text for each core module (bare-specifier import, e.g.
    /// <c>"console"</c> or <c>"http"</c>), keyed by specifier. Populate before
    /// calling <see cref="RunFromSource"/> or <see cref="RunFromFile"/>; entries
    /// added after construction are visible to subsequent load calls.
    /// </summary>
    public ScriptCoreModules CoreModules { get; } = new ScriptCoreModules();

    /// <summary>
    /// Host implementations for free-standing <c>native function</c>
    /// declarations, keyed by declared name. Populate before calling
    /// <see cref="RunFromSource"/> or <see cref="RunFromFile"/>.
    /// </summary>
    public ScriptNativeBindings NativeBindings { get; } = new ScriptNativeBindings();

    /// <summary>
    /// Host implementations for <c>native</c> method declarations, keyed by
    /// declaring class name and member name. Populate before calling
    /// <see cref="RunFromSource"/> or <see cref="RunFromFile"/>.
    /// </summary>
    public ScriptNativeMethodBindings NativeMethodBindings { get; } = new ScriptNativeMethodBindings();

    /// <inheritdoc/>
    public IAsyncOperationBinder? OperationBinder { get; set; }

    /// <summary>
    /// Creates a runtime with the default pipeline: real filesystem module
    /// loading and a core-module table backed by <see cref="CoreModules"/>.
    /// Populate <see cref="CoreModules"/> before calling any Run method to
    /// make bare-specifier imports available.
    /// </summary>
    public ProgramRuntime()
      : this(new FileSystemModuleFileReader())
    {
    }

    /// <summary>
    /// Creates a runtime with a custom module file reader — for hosts that
    /// provide modules from a source other than the real filesystem (e.g. an
    /// in-memory virtual file system, an asset bundle, or a network store)
    /// while keeping the rest of the default pipeline intact.
    /// </summary>
    public ProgramRuntime(IModuleFileReader moduleFileReader)
      : this(new ProgramLoader(
          new ALKScriptLexer(),
          new ALKScriptParser(),
          moduleFileReader,
          new DictionaryCoreModuleProvider(out var coreModules)))
    {
      CoreModules = coreModules;
    }

    /// <summary>
    /// Creates a runtime with an explicit <paramref name="loader"/> — for
    /// tests or advanced embeddings that need to inject a custom module loader.
    /// </summary>
    public ProgramRuntime(IProgramLoader loader)
    {
      _loader = loader;
    }

    /// <inheritdoc/>
    public ModuleGraph LoadFromSource(string source) => _loader.LoadFromSource(source);

    /// <inheritdoc/>
    public ModuleGraph LoadFromFile(string filePath) => _loader.Load(filePath);

    /// <inheritdoc/>
    public ProgramRun RunFromGraph(ModuleGraph graph, ScriptArguments? arguments = null) =>
      ProgramRun.Start(new CursorProgramEvaluator(NativeBindings, NativeMethodBindings, OperationBinder), graph, arguments);

    /// <inheritdoc/>
    public ProgramRun RunFromSource(string source, ScriptArguments? arguments = null) => RunFromGraph(LoadFromSource(source), arguments);

    /// <inheritdoc/>
    public ProgramRun RunFromFile(string filePath, ScriptArguments? arguments = null) => RunFromGraph(LoadFromFile(filePath), arguments);

    // -------------------------------------------------------------------------

    /// <summary>
    /// Bridges a mutable <see cref="ScriptCoreModules"/> dictionary to the
    /// <see cref="ICoreModuleProvider"/> contract expected by
    /// <see cref="ProgramLoader"/>. Modules are parsed on demand so entries
    /// added after construction are always visible to subsequent load calls.
    /// </summary>
    private sealed class DictionaryCoreModuleProvider : ICoreModuleProvider
    {
      private readonly ScriptCoreModules _modules;
      private readonly ALKScriptLexer _lexer = new ALKScriptLexer();
      private readonly ALKScriptParser _parser = new ALKScriptParser();

      internal DictionaryCoreModuleProvider(out ScriptCoreModules modules)
      {
        _modules = modules = new ScriptCoreModules();
      }

      public IReadOnlyCollection<string> AvailableModules => new List<string>(_modules.Keys);

      public ProgramNode GetModule(string specifier)
      {
        _modules.TryGetValue(specifier, out var source);
        return _parser.ParseTokens(_lexer.Tokenize(source!));
      }
    }
  }
}
