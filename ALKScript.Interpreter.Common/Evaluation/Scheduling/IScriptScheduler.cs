using System;

namespace ALKScript.Interpreter.Common.Evaluation.Scheduling
{
  /// <summary>
  /// The minimal scheduling contract the evaluator depends on: the ability to
  /// post a continuation onto the host thread. The evaluator calls
  /// <see cref="Enqueue"/> via its custom-awaitable wrappers (see
  /// <c>ScheduledTask</c>) — it never pumps or drives the queue itself.
  ///
  /// This lives in Common so that the evaluator can depend on it without
  /// pulling in the concrete scheduler implementation.
  /// </summary>
  public interface IScriptScheduler
  {
    /// <summary>
    /// Enqueues <paramref name="continuation"/> to run on the host thread.
    /// Called by the evaluator's custom awaitables to route every
    /// <c>await</c> continuation through this scheduler rather than the
    /// ambient <see cref="System.Threading.SynchronizationContext"/>.
    /// </summary>
    void Enqueue(Action continuation);
  }
}
