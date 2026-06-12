namespace ALKScript.Interpreter.Evaluator.Cursor
{
  /// <summary>
  /// A captured <see cref="TrailEntry"/> — part of the "Phase B" structural
  /// Capture/Restore design (docs/ASYNC_AWAIT_DESIGN.md Addendum 3).
  /// <see cref="EnvironmentId"/> references a <see cref="CapturedEnvironment.Id"/>
  /// in <see cref="CursorStructuralCaptureState.Environments"/>. Entries are
  /// stored leaf-to-root, matching <see cref="EvaluationCursor"/>'s internal
  /// <c>_trail</c> ordering.
  /// </summary>
  public sealed class CapturedTrailEntry
  {
    public int Index { get; set; }

    public int EnvironmentId { get; set; }

    /// <summary>The <c>try</c>/<c>finally</c> region's pending signal, if any (see <see cref="TrailEntry.PendingSignal"/>).</summary>
    public CapturedSignal? PendingSignal { get; set; }
  }
}
