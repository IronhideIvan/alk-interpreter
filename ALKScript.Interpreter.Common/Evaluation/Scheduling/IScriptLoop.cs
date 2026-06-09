namespace ALKScript.Interpreter.Common.Evaluation.Scheduling
{
  /// <summary>
  /// The host-facing scheduling contract: extends <see cref="IScriptScheduler"/>
  /// with the ability to drive the continuation queue. The evaluator only ever
  /// sees <see cref="IScriptScheduler"/>; hosts depend on <see cref="IScriptLoop"/>
  /// so they can pump or block-run scripts without the evaluator having access
  /// to those operations.
  ///
  /// A host that integrates its own job system implements this interface rather
  /// than subclassing the concrete <c>ScriptScheduler</c>.
  /// </summary>
  public interface IScriptLoop : IScriptScheduler
  {
    /// <summary>
    /// Runs every continuation that was queued at the moment this call began —
    /// a stable snapshot, in scheduling order (docs/ASYNC_AWAIT_DESIGN.md
    /// decision #7) — and no more: anything enqueued while pumping waits for
    /// the next call. This is the per-tick API a game host calls once per
    /// game-loop step. Returns the number of continuations it ran, so a host
    /// can tell whether this tick did any work.
    /// </summary>
    int Pump();

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
    /// real game host drives the scheduler through <see cref="Pump"/> on each
    /// tick instead.
    /// </para>
    /// </summary>
    void RunUntilComplete(ScriptEvaluation evaluation);
  }
}
