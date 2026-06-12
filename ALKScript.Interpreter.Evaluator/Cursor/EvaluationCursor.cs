using System;
using System.Collections.Generic;
using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Common.Evaluation;
using ALKScript.Interpreter.Common.Evaluation.Scheduling;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Evaluator.Cursor
{
  /// <summary>The outcome of <see cref="EvaluationCursor.Run"/>/<see cref="EvaluationCursor.Resume"/>/<see cref="EvaluationCursor.ResumeFaulted"/>.</summary>
  internal enum RunResult
  {
    Completed,
    Awaiting,
  }

  /// <summary>
  /// One level of the "resume trail" recorded when a top-level run suspends:
  /// which child (by statement/iteration/case/region index) a resumable
  /// construct was executing, and the <see cref="ScriptEnvironment"/> it was
  /// using for that child. <see cref="EvaluationCursor.Resume"/> walks the
  /// trail root-to-leaf (it is recorded leaf-to-root) to fast-forward back to
  /// the exact suspended statement without re-evaluating anything already
  /// evaluated.
  /// </summary>
  internal readonly struct TrailEntry
  {
    public int Index { get; }
    public ScriptEnvironment Environment { get; }

    /// <summary>
    /// For a <c>try</c>/<c>finally</c> region only: the <see cref="Signal"/>
    /// (return/throw/etc.) that was pending *before* the <c>finally</c> block
    /// started running — restored after <c>finally</c> completes unless
    /// <c>finally</c> itself raised a new one. Persisted here because it
    /// would otherwise be a lost local variable across suspension.
    /// </summary>
    public Signal? PendingSignal { get; }

    public TrailEntry(int index, ScriptEnvironment environment, Signal? pendingSignal = null)
    {
      Index = index;
      Environment = environment;
      PendingSignal = pendingSignal;
    }
  }

  /// <summary>
  /// A synchronous, resumable replacement for the <c>async Task</c>-based
  /// evaluator spine. Walks the AST node-by-node via <see cref="StepResult"/>;
  /// when an <c>await</c> on an unresolved <c>thunk</c>/<c>thunk&lt;T&gt;</c>
  /// is hit, the walk returns <see cref="RunResult.Awaiting"/> instead of
  /// blocking, and the host resumes it later via <see cref="Resume"/>.
  ///
  /// <see cref="Signal"/> is the orthogonal non-local-exit mechanism
  /// (break/continue/return/thrown/cancelled), unchanged from
  /// <see cref="IEvaluationContext"/> — checked after each sub-step exactly as
  /// it was checked after each <c>await</c> in the old evaluator.
  ///
  /// Suspension/resumption (Step 6 of the cursor-rewrite plan, docs:
  /// validated-nibbling-narwhal) is implemented via a "resume trail": when a
  /// statement's expression returns <see cref="StepResult.Awaiting"/>, every
  /// enclosing resumable construct (block/loop/if/switch/try) records, in
  /// <see cref="_trail"/>, which child it was executing and with which
  /// environment. <see cref="Resume"/> walks that trail back down to the
  /// exact suspended statement — re-entering only the chosen branches/
  /// iterations/regions, without re-evaluating already-evaluated expressions
  /// — substitutes the resumed value for the <c>await</c> that suspended, and
  /// continues normal execution from there.
  ///
  /// A called function/constructor body may itself suspend — its
  /// <see cref="ExecuteBlock"/> call participates in the same flat trail as
  /// any other block, with no dedicated trail entry of its own. Field/
  /// static-field initializers and native array-method callbacks remain
  /// restricted (see <see cref="CursorCallInvoker"/>'s <c>DisallowSuspension</c>).
  /// </summary>
  internal sealed class EvaluationCursor
  {
    private readonly CursorExpressionEvaluator _expressionEvaluator;
    private readonly CursorStatementExecutor _statementExecutor;
    private readonly CursorCallInvoker _callInvoker;

    private IReadOnlyList<Stmt>? _rootStatements;
    private ScriptEnvironment? _rootEnvironment;

    /// <summary>The suspended run's resume trail, leaf-to-root. Empty when not suspended.</summary>
    private List<TrailEntry> _trail = new();

    /// <summary>Entries recorded by a (possibly new) suspension during the current <see cref="Run"/>.</summary>
    private List<TrailEntry> _newTrail = new();

    /// <summary>
    /// Index into <see cref="_trail"/> of the next entry to consume,
    /// decremented as each enclosing construct fast-forwards into its
    /// resumed child. <c>-1</c> means "not resuming" — execute normally.
    /// </summary>
    private int _resumeCursor = -1;

    /// <summary>The value to substitute for the <c>await</c> that suspended, set by <see cref="Resume"/>.</summary>
    private ALKScriptValue? _resumeValue;

    /// <summary>
    /// The record-and-replay log (docs/ASYNC_AWAIT_DESIGN.md decision #17),
    /// carried over unchanged from the old evaluator's <c>IEvaluationContext.Log</c>/
    /// <c>TryReplayNext</c>/<c>RecordEntry</c>. During a replay run this starts
    /// pre-populated with <paramref name="replayLog"/>; entries beyond
    /// <see cref="_replayLength"/> are newly recorded during this run.
    /// </summary>
    private readonly List<OperationLogEntry> _log = new();
    private int _replayIndex;
    private readonly int _replayLength;

    public EvaluationCursor(IFunctionValueFactory functionValueFactory, IReadOnlyList<OperationLogEntry>? replayLog = null)
    {
      FunctionValueFactory = functionValueFactory;
      _expressionEvaluator = new CursorExpressionEvaluator(this, functionValueFactory);
      _statementExecutor = new CursorStatementExecutor(this);
      _callInvoker = new CursorCallInvoker(this);

      if (replayLog != null)
      {
        _log.AddRange(replayLog);
      }

      _replayLength = _log.Count;
    }

    /// <summary>The ordered log of every <c>async native</c> operation outcome recorded during this run. See <see cref="ProgramEvaluator.Log"/>.</summary>
    public IReadOnlyList<OperationLogEntry> Log => _log;

    /// <summary>Used by <see cref="CursorStatementExecutor"/> to create callable values for top-level/nested <see cref="FunctionDecl"/>/<see cref="ClassDecl"/> declarations.</summary>
    public IFunctionValueFactory FunctionValueFactory { get; }

    /// <summary>Consumes and returns the next replay-log entry, or <c>null</c> once the replay log is exhausted (live execution).</summary>
    public OperationLogEntry? TryReplayNext() => _replayIndex < _replayLength ? _log[_replayIndex++] : null;

    /// <summary>Appends a newly-settled operation's outcome to the log for future replay.</summary>
    public void RecordEntry(OperationLogEntry entry) => _log.Add(entry);

    /// <summary>
    /// The pending non-local exit (break/continue/return/thrown/cancelled), if
    /// any. Mirrors <see cref="IEvaluationContext.Signal"/> — set by
    /// statement-level constructs and checked by every caller after each
    /// sub-step.
    /// </summary>
    public Signal? Signal { get; set; }

    /// <summary>What <see cref="Resume"/>/<see cref="ResumeFaulted"/> will settle, while <see cref="RunResult.Awaiting"/>.</summary>
    public AwaitHandle? PendingAwait { get; private set; }

    /// <summary>Whether a resumable construct should fast-forward into a previously-recorded child instead of starting fresh.</summary>
    public bool IsResuming => _resumeCursor >= 0;

    /// <summary>Whether the next <see cref="AwaitExpr"/> reached should consume <see cref="TakeResumeValue"/> instead of evaluating its operand.</summary>
    public bool HasResumeValue => _resumeValue != null;

    /// <summary>
    /// Returns the environment of the next (root-to-leaf) entry of the resume
    /// trail without consuming it, or <c>null</c> if not <see cref="IsResuming"/>.
    /// Used by <see cref="CursorCallInvoker.Construct"/> to recover the
    /// <c>this</c> instance a suspended constructor body is about to resume
    /// into, before <see cref="ExecuteBlock"/> itself pops the entry.
    /// </summary>
    public ScriptEnvironment? PeekResumeEnvironment() => IsResuming ? _trail[_resumeCursor].Environment : null;

    /// <summary>
    /// Pops and returns the next (root-to-leaf) entry of the resume trail.
    /// Only valid while <see cref="IsResuming"/> is true.
    /// </summary>
    public TrailEntry PopResumeEntry()
    {
      var entry = _trail[_resumeCursor];
      _resumeCursor--;
      return entry;
    }

    /// <summary>
    /// Records that the construct currently executing child <paramref name="index"/>
    /// (with <paramref name="environment"/>) is suspending — called by every
    /// resumable construct on the way back up from a child that returned
    /// <see cref="StepResult.IsAwaiting"/>. Only called in "normal" (not
    /// resuming) mode, so entries accumulate leaf-first.
    /// </summary>
    public void RecordSuspend(int index, ScriptEnvironment environment)
    {
      _newTrail.Add(new TrailEntry(index, environment));
    }

    /// <summary>As <see cref="RecordSuspend(int, ScriptEnvironment)"/>, additionally persisting a <c>try</c>/<c>finally</c>'s pending signal.</summary>
    public void RecordSuspend(int index, ScriptEnvironment environment, Signal? pendingSignal)
    {
      _newTrail.Add(new TrailEntry(index, environment, pendingSignal));
    }

    /// <summary>
    /// Consumes and returns the value <see cref="Resume"/> was called with —
    /// the result of the <c>await</c> expression that suspended. Must be
    /// called at most once per resume (enforced implicitly: the leaf
    /// statement's single top-level <c>await</c>).
    /// </summary>
    public ALKScriptValue TakeResumeValue()
    {
      var value = _resumeValue ?? NullValue.Instance;
      _resumeValue = null;
      return value;
    }

    /// <summary>
    /// Evaluates <paramref name="expression"/> against <paramref name="environment"/>.
    /// Returns <see cref="StepResult.Completed"/> with <see cref="NullValue.Instance"/>
    /// without recursing if <see cref="Signal"/> is already set, mirroring the
    /// old evaluator's "if (_context.Signal != null) return NullValue.Instance;"
    /// guard at the top of <c>Eval</c>.
    /// </summary>
    public StepResult Eval(Expr expression, ScriptEnvironment environment) => Eval(expression, environment, allowSuspend: false);

    /// <summary>
    /// As <see cref="Eval(Expr, ScriptEnvironment)"/>, but additionally allows
    /// an <see cref="AwaitExpr"/> directly at <paramref name="expression"/> to
    /// return <see cref="StepResult.Awaiting"/> on an unresolved thunk — used
    /// for the await-placement-restricted positions of plan §4 (the entire
    /// initializer of a <see cref="VariableDecl"/>, the entire value of a
    /// <see cref="ReturnStmt"/>/<see cref="ThrowStmt"/>, or the entire
    /// expression of an <see cref="ExpressionStmt"/>).
    /// </summary>
    public StepResult Eval(Expr expression, ScriptEnvironment environment, bool allowSuspend)
    {
      if (Signal != null)
      {
        return StepResult.Completed(NullValue.Instance);
      }

      return _expressionEvaluator.Eval(expression, environment, allowSuspend);
    }

    /// <summary>
    /// Executes <paramref name="statement"/> against <paramref name="environment"/>.
    /// Mirrors the no-op short-circuit in <see cref="Eval"/>: if
    /// <see cref="Signal"/> is already set, returns immediately without
    /// recursing.
    /// </summary>
    public StepResult Execute(Stmt statement, ScriptEnvironment environment)
    {
      if (Signal != null)
      {
        return StepResult.Completed(NullValue.Instance);
      }

      return _statementExecutor.Execute(statement, environment);
    }

    /// <summary>Executes each statement in order, stopping early on suspension or a pending <see cref="Signal"/>.</summary>
    public StepResult ExecuteBlock(IReadOnlyList<Stmt> statements, ScriptEnvironment environment)
    {
      return _statementExecutor.ExecuteBlock(statements, environment);
    }

    /// <summary>Calls <paramref name="callee"/> with <paramref name="arguments"/>.</summary>
    public StepResult Call(ALKScriptValue callee, IReadOnlyList<ALKScriptValue> arguments, ALKScriptToken site)
    {
      return _callInvoker.Call(callee, arguments, site);
    }

    /// <summary>Constructs a new instance of <paramref name="classValue"/>.</summary>
    public StepResult Construct(ClassValue classValue, IReadOnlyList<ALKScriptValue> arguments, IReadOnlyList<TypeNode> typeArguments, ALKScriptToken site)
    {
      return _callInvoker.Construct(classValue, arguments, typeArguments, site);
    }

    /// <summary>
    /// Begins evaluating <paramref name="statements"/> against <paramref name="environment"/>
    /// as the outermost (top-level) sequence — the only level at which
    /// suspension is currently supported. Returns <see cref="RunResult.Awaiting"/>
    /// if execution hits an unresolved <c>await</c> in an allowed position
    /// (see <see cref="PendingAwait"/>), otherwise <see cref="RunResult.Completed"/>.
    /// </summary>
    public RunResult Start(IReadOnlyList<Stmt> statements, ScriptEnvironment environment)
    {
      _rootStatements = statements;
      _rootEnvironment = environment;
      return Run();
    }

    private RunResult Run()
    {
      var step = ExecuteBlock(_rootStatements!, _rootEnvironment!);

      if (step.IsAwaiting)
      {
        PendingAwait = step.Handle;
        _trail = _newTrail;
        _newTrail = new List<TrailEntry>();
        return RunResult.Awaiting;
      }

      PendingAwait = null;
      _trail.Clear();
      _newTrail.Clear();
      _resumeCursor = -1;
      return RunResult.Completed;
    }

    /// <summary>
    /// Resumes a suspended run with the settled result of the pending
    /// <c>await</c> — validated against <see cref="AwaitHandle.ElementType"/>,
    /// per <see cref="TypeChecking.MatchesType"/>, exactly as the old
    /// evaluator's <c>ValidateThunkResult</c> did.
    /// </summary>
    public RunResult Resume(ALKScriptValue value)
    {
      var pending = PendingAwait ?? throw new InvalidOperationException("EvaluationCursor.Resume called while not awaiting.");

      if (pending.ElementType != null && !TypeChecking.MatchesType(value, pending.ElementType, _rootEnvironment!, pending.Site))
      {
        throw new RuntimeException(pending.Site, $"Operation declared 'thunk<{pending.ElementType}>' resolved to a value of type '{value.TypeName}', expected '{pending.ElementType}'.");
      }

      if (pending.Operation != null)
      {
        _log.Add(OperationLogEntry.FromResult(pending.Operation, value));
      }

      _resumeValue = value;
      _resumeCursor = _trail.Count - 1;
      PendingAwait = null;
      return Run();
    }

    /// <summary>
    /// Resumes a suspended run by raising <paramref name="faultMessage"/> as a
    /// <see cref="SignalKind.Thrown"/> signal at the point of suspension —
    /// mirroring the old evaluator's "catch (Exception) { Signal =
    /// Signal.Thrown(...) }" around <c>AwaitTask</c>/<c>AwaitPending</c>.
    /// </summary>
    public RunResult ResumeFaulted(string faultMessage)
    {
      if (PendingAwait?.Operation != null)
      {
        _log.Add(OperationLogEntry.FromFault(PendingAwait.Operation, faultMessage));
      }

      PendingAwait = null;
      Signal = ALKScript.Interpreter.Common.Evaluation.Signal.Thrown(new StringValue(faultMessage));
      _resumeValue = NullValue.Instance;
      _resumeCursor = _trail.Count - 1;
      return Run();
    }
  }
}
