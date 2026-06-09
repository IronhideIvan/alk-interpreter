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
    private int _replayLength; // fixed at construction; live-recorded entries never qualify as replay

    /// <summary>
    /// Creates an evaluator.
    ///
    /// <paramref name="nativeBindings"/> supplies the host implementations for
    /// free-standing <c>native function</c> declarations, keyed by declared
    /// name; <paramref name="nativeMethodBindings"/> supplies them for
    /// <c>native</c> methods, keyed by declaring class and member name (see
    /// <see cref="ScriptNativeMethodBindings"/> for why methods need their own,
    /// class-scoped table — e.g. an <c>Array</c> and a <c>Set</c> can each
    /// declare a native <c>add</c> without colliding, and each implementation
    /// receives the receiving instance so it can back the class with real,
    /// host-managed storage). Both the entry module's own <c>native</c>
    /// declarations and any in the graph's <see cref="ModuleGraph.GlobalPreludes"/>
    /// resolve against these tables. A <c>native</c> declaration with no
    /// matching binding fails with a <see cref="RuntimeException"/> as soon as
    /// it is declared (functions) or first accessed (methods), so a runtime
    /// that supplies a prelude with <c>native</c> members is responsible for
    /// registering bindings for them here.
    ///
    /// This evaluator is a pure executor: it never lexes or parses anything
    /// itself — and so never references the concrete <c>ALKScriptLexer</c>/
    /// <c>ALKScriptParser</c> types (which live in the Lexer/Parser projects).
    /// "Compile source -&gt; structured representation" is entirely
    /// <c>IProgramLoader</c>'s job, including the runtime-supplied "true
    /// global" prelude (see <see cref="ModuleGraph.GlobalPreludes"/> and
    /// <c>IGlobalPreludeProvider</c> in the Parser project) — this mirrors how
    /// core-module resolution is pushed out to an injected
    /// <c>ICoreModuleProvider</c> rather than hardcoded here: the loader
    /// provides the mechanism (parse-and-assemble), the runtime supplies the
    /// content, and the evaluator just walks the result.
    /// </summary>
    /// <param name="operationBinder">
    /// The host's <see cref="Common.Evaluation.Scheduling.IAsyncOperationBinder"/>
    /// — the integration seam for free-standing <c>native async</c>
    /// declarations specifically (see <see cref="FunctionValueFactory"/>'s
    /// constructor docs for why they're bound separately from
    /// <paramref name="nativeBindings"/>). A runtime that declares no
    /// <c>native async</c> functions can omit it.
    /// </param>
    /// <param name="replayLog">
    /// A previously-captured operation log to replay (see
    /// <see cref="Log"/> and docs/ASYNC_AWAIT_DESIGN.md decision #17). When
    /// supplied, each <c>await</c> on a <see cref="Common.Evaluation.Values.PendingOperationValue"/>
    /// consumes the next log entry positionally — returning its recorded result
    /// or fault without starting the operation — until the log is exhausted,
    /// at which point live execution resumes and new entries are appended.
    /// Omit (or pass <c>null</c>) for a fresh run with no prior log.
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

    // Explicit implementation of IEvaluator so the Task-returning member is
    // not visible on ProgramEvaluator directly (which would invite the
    // CS4014 "call not awaited" warning in host code). The public surface
    // exposes only the ScriptEvaluation overload above.
    Task IEvaluator.Evaluate(ModuleGraph graph) => EvaluateCore(graph);

    private async Task EvaluateCore(ModuleGraph graph)
    {
      var globals = new ScriptEnvironment();

      // Seed the runtime-supplied global prelude(s) first: ordinary top-level
      // declarations executed into the root environment, in order, so their
      // bindings are true globals — visible to (and shadowable by) the entry
      // module without any "import" or per-script "native function"
      // re-declaration. These were compiled once by the loader (see
      // ModuleGraph.GlobalPreludes), so there's nothing to parse here.
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

      foreach (var declaration in graph.EntryModule.Program.Declarations)
      {
        await _statements.Execute(declaration, globals);

        if (_signal != null)
        {
          break;
        }
      }

      // Fire-and-forget any async native operations that were called but never
      // awaited — the "Discard" path (see IAsyncOperationBinder.Discard and
      // docs/ASYNC_AWAIT_DESIGN.md decision #10). Skipped if the script was
      // cancelled: a cancelled script did not "finish" — it was cut short.
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

    Task<ALKScriptValue> IEvaluationContext.Construct(ClassValue classValue, IReadOnlyList<ALKScriptValue> arguments, ALKScriptToken site)
      => _calls.Construct(classValue, arguments, site);

    OperationLogEntry? IEvaluationContext.TryReplayNext()
      => _replayIndex < _replayLength ? _log[_replayIndex++] : null;

    void IEvaluationContext.RecordEntry(OperationLogEntry entry) => _log.Add(entry);
  }
}
