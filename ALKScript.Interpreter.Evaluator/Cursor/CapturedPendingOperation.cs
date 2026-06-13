namespace ALKScript.Interpreter.Evaluator.Cursor
{
  /// <summary>
  /// One entry of <see cref="CursorStructuralCaptureState.PendingOperations"/> —
  /// a <c>PendingOperationValue</c>/<c>ThunkValue</c> reachable from a local
  /// variable (and possibly also the suspending <c>await</c>'s own operand via
  /// <see cref="CapturedPendingAwait.OperationRef"/>), "Phase C"
  /// (docs/ASYNC_AWAIT_DESIGN.md Addendum 3).
  ///
  /// Reuses the existing <see cref="CapturedAwaitElement"/> discriminated union:
  /// <see cref="CapturedAwaitElement.Resolved"/> for a settled
  /// <c>ThunkValue</c>/<c>PendingOperationValue</c>, <see cref="CapturedAwaitElement.Fault"/>
  /// for a faulted one, and <see cref="CapturedAwaitElement.Reissue"/> for a
  /// <c>PendingOperationValue</c> with a recoverable <see cref="ALKScript.Interpreter.Common.Evaluation.Scheduling.PendingOperation"/>.
  /// </summary>
  public sealed class CapturedPendingOperation
  {
    public CapturedAwaitElement Element { get; }

    /// <summary>
    /// For <see cref="CapturedAwaitElement.Reissue"/>: whether
    /// <c>PendingOperationValue.HasStarted</c> was <c>true</c> at capture time
    /// (i.e. <c>Start()</c> had been called, even if not yet settled).
    /// <c>false</c> means the local held a not-yet-started operation
    /// (e.g. <c>var op = fetch();</c> before any <c>await op</c>) — Restore
    /// reconstructs it without calling <c>IAsyncOperationBinder.Start</c>, so
    /// the script's own subsequent <c>await</c> triggers the start. Ignored
    /// for <see cref="CapturedAwaitElement.Resolved"/>/<see cref="CapturedAwaitElement.Fault"/>.
    /// </summary>
    public bool WasStarted { get; }

    public CapturedPendingOperation(CapturedAwaitElement element, bool wasStarted)
    {
      Element = element;
      WasStarted = wasStarted;
    }
  }
}
