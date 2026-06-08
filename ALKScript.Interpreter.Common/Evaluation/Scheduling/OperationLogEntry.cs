using ALKScript.Interpreter.Common.Evaluation.Values;

namespace ALKScript.Interpreter.Common.Evaluation.Scheduling
{
  /// <summary>
  /// One entry in a script instance's replay log — a fully-evaluated snapshot
  /// of one <c>async native</c> operation's outcome (see
  /// docs/ASYNC_AWAIT_DESIGN.md, decision #17 / record-and-replay save/load).
  ///
  /// Each entry records:
  /// - <em>which</em> operation ran (<see cref="Operation"/>, the same
  ///   <see cref="PendingOperation"/> descriptor the binder was handed), and
  /// - <em>how it settled</em> — either a <see cref="Result"/> value (on
  ///   success) or a <see cref="FaultMessage"/> string (on failure). The fault
  ///   is stored as a message rather than an exception so the entry is trivially
  ///   serializable: an exception's stack trace and type information are
  ///   process-specific and not meaningful across a save/reload boundary.
  ///
  /// Entries are created by the evaluator during live execution and consumed
  /// positionally (in log order) during replay: the N-th <c>await</c> on a
  /// <c>PendingOperationValue</c> during replay consumes the N-th log entry,
  /// returning its recorded result or fault without starting the operation.
  /// This positional contract relies on the script being deterministic between
  /// <c>await</c> points (decisions #15/#16).
  /// </summary>
  public sealed class OperationLogEntry
  {
    public PendingOperation Operation { get; }

    /// <summary>The value the operation resolved to, or <c>null</c> if it faulted.</summary>
    public ALKScriptValue? Result { get; }

    /// <summary>The fault message if the operation faulted, or <c>null</c> if it succeeded.</summary>
    public string? FaultMessage { get; }

    public bool IsFaulted => FaultMessage != null;

    private OperationLogEntry(PendingOperation operation, ALKScriptValue? result, string? faultMessage)
    {
      Operation = operation;
      Result = result;
      FaultMessage = faultMessage;
    }

    public static OperationLogEntry FromResult(PendingOperation operation, ALKScriptValue result)
      => new OperationLogEntry(operation, result, null);

    public static OperationLogEntry FromFault(PendingOperation operation, string faultMessage)
      => new OperationLogEntry(operation, null, faultMessage);
  }
}
