namespace ALKScript.Interpreter.Common.Evaluation.Scheduling
{
  /// <summary>
  /// The single-threaded "host thread" every script's <c>await</c> resumes
  /// on — see docs/ASYNC_AWAIT_DESIGN.md decision #2. Multiplexes every
  /// script's <c>await</c> continuations onto one thread, so a host can run
  /// scripts to completion (or drive them tick-by-tick) without ever letting
  /// script code run concurrently with itself or the game loop.
  ///
  /// This lives in Common (rather than alongside its implementation) so that
  /// hosts and other layers can depend on the scheduling contract without
  /// depending on the evaluator's internals.
  /// </summary>
  public interface IScriptScheduler
  {
    /// <summary>
    /// Runs every continuation that was queued at the moment this call began —
    /// a stable snapshot, in scheduling order (decision #7) — and no more:
    /// anything queued while pumping (e.g. the next leg of a "pause"-style
    /// yield) waits for the next call. This is the per-tick API a real host
    /// calls once per game-loop step. Returns the number of continuations it
    /// ran, so a host can tell whether this tick did anything.
    /// </summary>
    int Pump();
  }
}
