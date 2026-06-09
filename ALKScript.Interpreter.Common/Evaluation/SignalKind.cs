namespace ALKScript.Interpreter.Common.Evaluation
{
  /// <summary>The kind of non-local exit a <see cref="Signal"/> represents.</summary>
  public enum SignalKind
  {
    /// <summary>Unwinding to the nearest enclosing function/constructor call ("return").</summary>
    Return,

    /// <summary>Unwinding to the nearest enclosing "try"/"catch", or the top level ("throw").</summary>
    Thrown,

    /// <summary>Unwinding to the nearest enclosing loop ("break").</summary>
    Break,

    /// <summary>Skipping to the next iteration of the nearest enclosing loop ("continue").</summary>
    Continue,

    /// <summary>
    /// Unwinding the entire script in response to an external stop/cancellation
    /// request (e.g. the host stopping a running script). Unlike <see cref="Thrown"/>,
    /// a "Cancelled" signal is not catchable — it deliberately bypasses "catch"
    /// clauses while still running "finally" blocks on its way out, so resources
    /// get cleaned up but script logic cannot suppress or react to the cancellation.
    /// </summary>
    Cancelled
  }
}
