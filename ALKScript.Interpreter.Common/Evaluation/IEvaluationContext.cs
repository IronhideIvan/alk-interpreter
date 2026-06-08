using System.Collections.Generic;
using System.Threading.Tasks;
using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Common.Evaluation.Scheduling;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Common.Evaluation
{
  /// <summary>
  /// The shared surface that <see cref="StatementExecutor"/>,
  /// <see cref="ExpressionEvaluator"/> and <see cref="CallInvoker"/> recurse
  /// through, plus the pending-signal slot they coordinate on to implement
  /// "return"/"throw"/"cancelled" unwinding without .NET exceptions.
  ///
  /// The three components call into each other (statements evaluate
  /// expressions, expressions perform calls, calls execute statement blocks),
  /// which would otherwise force them into a constructor cycle. Routing those
  /// calls through this interface — implemented by <see cref="ProgramEvaluator"/>,
  /// which composes and wires up the three — lets each be built independently
  /// and breaks the cycle.
  ///
  /// Every recursive operation here is <see cref="Task"/>-returning rather than
  /// synchronous. This is what lets <c>await</c> expressions suspend evaluation
  /// mid-tree-walk and later resume exactly where they left off: the C# compiler
  /// turns each <c>async</c> method in the recursive chain into a continuation
  /// -passing state machine for free, so a pending host operation can be awaited
  /// without losing or duplicating any evaluation state. "Return"/"Throw"/
  /// "Cancelled" unwinding (via the <see cref="Signal"/> slot) and suspension
  /// (via these methods returning <see cref="Task"/>) are deliberately
  /// orthogonal mechanisms layered on top of each other.
  /// </summary>
  public interface IEvaluationContext
  {
    /// <summary>
    /// A pending non-local exit ("return", "throw", or "cancelled") raised
    /// while executing the statement or evaluating the expression currently in
    /// progress. Checked after each sub-execution/sub-evaluation so control can
    /// unwind to the right handler (a function call, a "try", or the top level).
    /// </summary>
    Signal? Signal { get; set; }

    Task Execute(Stmt statement, ScriptEnvironment environment);

    Task ExecuteBlock(IReadOnlyList<Stmt> statements, ScriptEnvironment environment);

    Task<ALKScriptValue> Eval(Expr expression, ScriptEnvironment environment);

    Task<ALKScriptValue> Call(ALKScriptValue callee, IReadOnlyList<ALKScriptValue> arguments, ALKScriptToken site);

    Task<ALKScriptValue> Construct(ClassValue classValue, IReadOnlyList<ALKScriptValue> arguments, ALKScriptToken site);

    /// <summary>
    /// Consumes and returns the next entry from the replay log, or <c>null</c>
    /// if the log is exhausted (live mode). Called at each <c>await</c> on a
    /// <see cref="PendingOperationValue"/> — the positional log contract.
    /// </summary>
    OperationLogEntry? TryReplayNext();

    /// <summary>
    /// Appends <paramref name="entry"/> to the operation log, recording an
    /// <c>async native</c> operation's outcome for future replay.
    /// </summary>
    void RecordEntry(OperationLogEntry entry);
  }
}
