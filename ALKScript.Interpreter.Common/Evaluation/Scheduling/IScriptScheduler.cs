using System;

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

    /// <summary>
    /// Enqueues <paramref name="continuation"/> to run on the next
    /// <see cref="Pump"/> call. Called by the evaluator's custom awaitables
    /// (see <c>ScheduledTask</c>) to route every <c>await</c> continuation
    /// through this scheduler rather than the ambient
    /// <see cref="System.Threading.SynchronizationContext"/>.
    /// </summary>
    void Enqueue(Action continuation);

    /// <summary>
    /// Blocks the calling thread, pumping continuations as they arrive, until
    /// <paramref name="evaluation"/> runs to completion (or faults). Any
    /// exception thrown by the script is rethrown on the calling thread,
    /// unwrapped from its <see cref="System.Threading.Tasks.Task"/> wrapper
    /// exactly as a direct <c>await</c> would.
    ///
    /// <para>
    /// This is a convenience for embeddings that have no game loop of their
    /// own — tools, tests, and "run this script synchronously" scenarios. A
    /// real game host should never call this; it drives the scheduler through
    /// <see cref="Pump"/> on each tick instead.
    /// </para>
    /// </summary>
    void RunUntilComplete(ScriptEvaluation evaluation);
  }
}
