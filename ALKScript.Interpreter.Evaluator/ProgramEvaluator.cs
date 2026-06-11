using System.Collections.Generic;
using System.Threading.Tasks;
using ALKScript.Interpreter.Common;
using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Common.Evaluation;
using ALKScript.Interpreter.Common.Evaluation.Scheduling;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Common.Modules;
using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Evaluator
{
  /// <summary>
  /// Tree-walking evaluator that executes a <see cref="ModuleGraph"/> by
  /// running the entry module's top-level declarations and statements,
  /// producing/consuming <see cref="ALKScriptValue"/>s as it goes.
  ///
  /// The actual tree-walking is composed from three collaborators —
  /// <see cref="StatementExecutor"/>, <see cref="ExpressionEvaluator"/> and
  /// <see cref="CallInvoker"/> — which call into each other and share the
  /// pending-signal slot used for "return"/"throw" unwinding. This class wires
  /// them together by implementing <see cref="IEvaluationContext"/>, the
  /// interface they recurse through; that indirection is what lets three
  /// mutually-dependent collaborators be constructed without a cycle.
  ///
  /// This evaluator is a pure executor: it never lexes or parses anything, and
  /// never references the concrete lexer/parser types. Turning source into a
  /// <see cref="ModuleGraph"/> — including the runtime-supplied global prelude
  /// (<see cref="ModuleGraph.GlobalPreludes"/>) and core-module resolution — is
  /// <c>IProgramLoader</c>'s job; this class just walks the result.
  /// </summary>
  public class ProgramEvaluator : IEvaluator, IEvaluationContext
  {
    private readonly IScriptScheduler? _scheduler;
    private readonly IFunctionValueFactory _functionValueFactory;
    private readonly IStatementExecutor _statements;
    private readonly IExpressionEvaluator _expressions;
    private readonly ICallInvoker _calls;

    private Signal? _signal;

    private readonly List<OperationLogEntry> _log = new List<OperationLogEntry>();
    private int _replayIndex;
    private int _replayLength; // Fixed at construction; live-recorded entries never count as replay.

    /// <summary>
    /// Creates an evaluator.
    /// </summary>
    /// <param name="nativeBindings">
    /// Host implementations for free-standing <c>native function</c>
    /// declarations, keyed by declared name. A <c>native</c> declaration with
    /// no matching binding fails with a <see cref="RuntimeException"/> as soon
    /// as it is declared.
    /// </param>
    /// <param name="nativeMethodBindings">
    /// Host implementations for <c>native</c> methods, keyed by declaring
    /// class and member name (see <see cref="ScriptNativeMethodBindings"/> for
    /// why methods need their own class-scoped table). Fails the same way as
    /// <paramref name="nativeBindings"/>, but on first access rather than
    /// declaration.
    /// </param>
    /// <param name="operationBinder">
    /// The host's <see cref="Common.Evaluation.Scheduling.IAsyncOperationBinder"/>
    /// — the integration seam for free-standing <c>native async</c>
    /// declarations (see <see cref="FunctionValueFactory"/> for why they're
    /// bound separately from <paramref name="nativeBindings"/>). Omit if the
    /// script declares no <c>native async</c> functions.
    /// </param>
    /// <param name="replayLog">
    /// A previously-captured operation log to replay (see <see cref="Log"/>
    /// and docs/ASYNC_AWAIT_DESIGN.md decision #17). Each <c>await</c> on a
    /// <see cref="Common.Evaluation.Values.PendingOperationValue"/> consumes
    /// the next entry positionally — returning its recorded result or fault
    /// without starting the operation — until the log is exhausted, after
    /// which live execution resumes and appends new entries. Omit for a fresh
    /// run with no prior log.
    /// </param>
    public ProgramEvaluator(ScriptNativeBindings? nativeBindings = null, ScriptNativeMethodBindings? nativeMethodBindings = null, Common.Evaluation.Scheduling.IAsyncOperationBinder? operationBinder = null, IReadOnlyList<OperationLogEntry>? replayLog = null, IScriptScheduler? scheduler = null)
      : this(new FunctionValueFactory(nativeBindings, nativeMethodBindings, operationBinder), scheduler)
    {
      if (replayLog != null) _log.AddRange(replayLog);
      _replayLength = _log.Count;
    }

    /// <summary>
    /// Creates an evaluator with an explicit <see cref="IFunctionValueFactory"/>,
    /// e.g. for testing or to supply a host-specific binding strategy.
    /// </summary>
    public ProgramEvaluator(IFunctionValueFactory functionValueFactory, IScriptScheduler? scheduler = null)
      : this(functionValueFactory, new EvaluationComponentFactory(), scheduler)
    {
    }

    /// <summary>
    /// Creates an evaluator with explicit <see cref="IFunctionValueFactory"/> and
    /// <see cref="IEvaluationComponentFactory"/> implementations. Internal — the
    /// component factory deals in the internal collaborator interfaces — but
    /// reachable from tests via <c>InternalsVisibleTo</c>.
    /// </summary>
    internal ProgramEvaluator(IFunctionValueFactory functionValueFactory, IEvaluationComponentFactory componentFactory, IScriptScheduler? scheduler = null)
    {
      _scheduler = scheduler;
      _functionValueFactory = functionValueFactory;
      _statements = componentFactory.CreateStatementExecutor(this, functionValueFactory);
      _expressions = componentFactory.CreateExpressionEvaluator(this, functionValueFactory);
      _calls = componentFactory.CreateCallInvoker(this);
    }

    /// <summary>
    /// Starts evaluating <paramref name="graph"/> and returns an opaque
    /// <see cref="ScriptEvaluation"/> handle. Drive progress by calling
    /// <see cref="IScriptLoop.Pump"/> on each game-loop tick.
    /// </summary>
    public ScriptEvaluation Evaluate(ModuleGraph graph) => new ScriptEvaluation(EvaluateCore(graph));

    private async Task EvaluateCore(ModuleGraph graph)
    {
      var globals = new ScriptEnvironment();

      // Seed the runtime-supplied global prelude(s) first, so their bindings
      // are true globals visible to every module without an "import".
      // Already compiled by the loader (ModuleGraph.GlobalPreludes).
      foreach (var program in graph.GlobalPreludes)
      {
        foreach (var declaration in program.Declarations)
        {
          await _statements.Execute(declaration, globals);

          if (_signal != null)
          {
            break;
          }
        }

        if (_signal != null)
        {
          break;
        }
      }

      _signal = null;

      // Execute the entry module. Imported modules run on demand, depth-first,
      // with each module's environment cached so shared dependencies only run once.
      var moduleEnvs = new Dictionary<string, ScriptEnvironment>();
      await ExecuteModule(graph.EntryModule, graph, globals, moduleEnvs);

      // Fire-and-forget any async native operations that were called but never
      // awaited (IAsyncOperationBinder.Discard, decision #10). Skipped on
      // cancellation — a cancelled script didn't "finish", it was cut short.
      if (_signal?.Kind != SignalKind.Cancelled)
      {
        _functionValueFactory.DiscardPending(_ => { });
      }

      if (_signal is { Kind: SignalKind.Thrown } thrown)
      {
        _signal = null;
        throw new RuntimeException(
          AstTokenLocator.EndOfFile,
          $"Uncaught exception: {Operators.Stringify(thrown.Value)}");
      }

      // A stray top-level "return" simply ends the module's execution.
      _signal = null;
    }

    /// <summary>
    /// Executes <paramref name="module"/> in its own environment, first
    /// recursively executing any modules it imports (each only once), then
    /// binding the imported names into the module's environment, and finally
    /// running the module's own top-level declarations. Does nothing if the
    /// module has already been executed (its identifier is in
    /// <paramref name="moduleEnvs"/>).
    /// </summary>
    private async Task ExecuteModule(
      LoadedModule module,
      ModuleGraph graph,
      ScriptEnvironment globals,
      Dictionary<string, ScriptEnvironment> moduleEnvs)
    {
      if (moduleEnvs.ContainsKey(module.Identifier))
      {
        return;
      }

      // Cache before executing imports so diamond dependencies (A imports B and
      // C, B also imports C) only execute C once.
      var env = new ScriptEnvironment(globals);
      moduleEnvs[module.Identifier] = env;

      foreach (var import in module.Program.Imports)
      {
        string resolvedId = module.ImportResolutions[import.Source.Lexeme];
        LoadedModule dependency = graph.Modules[resolvedId];

        await ExecuteModule(dependency, graph, globals, moduleEnvs);

        if (_signal != null)
        {
          return;
        }

        BindImport(import, moduleEnvs[resolvedId], env);
      }

      // Re-exports ("export { Foo } from './foo';") are resolved like imports:
      // execute the target module, then copy the named bindings into this
      // module's environment so they're usable here and visible to importers.
      foreach (var declaration in module.Program.Declarations)
      {
        if (declaration is ReExportDecl reExport)
        {
          string resolvedId = module.ImportResolutions[reExport.Source.Lexeme];
          LoadedModule dependency = graph.Modules[resolvedId];

          await ExecuteModule(dependency, graph, globals, moduleEnvs);

          if (_signal != null)
          {
            return;
          }

          BindNamedSpecifiers(reExport.Specifiers, moduleEnvs[resolvedId], env);
        }
      }

      foreach (var declaration in module.Program.Declarations)
      {
        await _statements.Execute(declaration, env);

        if (_signal != null)
        {
          return;
        }
      }
    }

    /// <summary>
    /// Binds the names brought in by <paramref name="import"/> from
    /// <paramref name="sourceEnv"/> into <paramref name="targetEnv"/>.
    /// </summary>
    private static void BindImport(ImportDecl import, ScriptEnvironment sourceEnv, ScriptEnvironment targetEnv)
    {
      switch (import.Clause)
      {
        case NamedImportsClause namedImports:
          BindNamedSpecifiers(namedImports.Specifiers, sourceEnv, targetEnv);
          break;

        case NamespaceImportClause namespaceImport:
          var members = new Dictionary<string, ALKScriptValue>();
          foreach (var entry in sourceEnv.OwnBindings)
          {
            members[entry.Key] = entry.Value;
          }
          targetEnv.Define(namespaceImport.Alias.Lexeme, new NamespaceValue(namespaceImport.Alias.Lexeme, members));
          break;
      }
    }

    /// <summary>
    /// Binds each named specifier's value from <paramref name="sourceEnv"/>
    /// into <paramref name="targetEnv"/> under its local name (the alias, if
    /// any, otherwise the original name) — shared by named imports and
    /// re-export "from" clauses.
    /// </summary>
    private static void BindNamedSpecifiers(IReadOnlyList<ImportSpecifier> specifiers, ScriptEnvironment sourceEnv, ScriptEnvironment targetEnv)
    {
      foreach (var specifier in specifiers)
      {
        string exportedName = specifier.Name.Lexeme;
        string localName = specifier.Alias?.Lexeme ?? exportedName;

        if (!sourceEnv.TryGet(exportedName, out ALKScriptValue value))
        {
          throw new RuntimeException(specifier.Name, $"Module does not export '{exportedName}'.");
        }

        targetEnv.Define(localName, value);
      }
    }

    // ---------------------------------------------------------------------
    // Replay log — the record-and-replay save/load surface (decision #17).
    // ---------------------------------------------------------------------

    /// <summary>
    /// The ordered log of every <c>async native</c> operation outcome recorded
    /// during this run, for persistence and future replay (see
    /// docs/ASYNC_AWAIT_DESIGN.md decision #17). During a replay run this list
    /// starts pre-populated with the saved entries and grows as execution
    /// advances past the last saved point into live territory.
    /// </summary>
    public IReadOnlyList<OperationLogEntry> Log => _log;

    // ---------------------------------------------------------------------
    // IEvaluationContext — routes recursive calls between the collaborators
    // and exposes the pending-signal slot they coordinate on.
    // ---------------------------------------------------------------------

    IScriptScheduler? IEvaluationContext.Scheduler => _scheduler;

    Signal? IEvaluationContext.Signal
    {
      get => _signal;
      set => _signal = value;
    }

    Task IEvaluationContext.Execute(Stmt statement, ScriptEnvironment environment)
      => _statements.Execute(statement, environment);

    Task IEvaluationContext.ExecuteBlock(IReadOnlyList<Stmt> statements, ScriptEnvironment environment)
      => _statements.ExecuteBlock(statements, environment);

    Task<ALKScriptValue> IEvaluationContext.Eval(Expr expression, ScriptEnvironment environment)
      => _expressions.Eval(expression, environment);

    Task<ALKScriptValue> IEvaluationContext.Call(ALKScriptValue callee, IReadOnlyList<ALKScriptValue> arguments, ALKScriptToken site)
      => _calls.Call(callee, arguments, site);

    Task<ALKScriptValue> IEvaluationContext.Construct(ClassValue classValue, IReadOnlyList<ALKScriptValue> arguments, IReadOnlyList<TypeNode> typeArguments, ALKScriptToken site)
      => _calls.Construct(classValue, arguments, typeArguments, site);

    OperationLogEntry? IEvaluationContext.TryReplayNext()
      => _replayIndex < _replayLength ? _log[_replayIndex++] : null;

    void IEvaluationContext.RecordEntry(OperationLogEntry entry) => _log.Add(entry);
  }
}
