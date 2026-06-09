using System.Threading.Tasks;

namespace ALKScript.Interpreter.Common.Evaluation.Scheduling
{
  /// <summary>
  /// An opaque handle to a running script evaluation, returned by
  /// <c>ProgramEvaluator.Evaluate</c>.
  ///
  /// Host code never needs to inspect this value: progress is driven by
  /// calling <see cref="IScriptScheduler.Pump"/> on each game-loop tick and
  /// observing the side effects of the script (native bindings, output, etc.).
  ///
  /// The type is deliberately not a <see cref="Task"/> or
  /// <see cref="ValueTask"/> so that calling <c>Evaluate(graph)</c> without
  /// waiting for the result does not produce compiler warning CS4014 — the
  /// warning exists to protect callers from accidentally ignoring an async
  /// result, but for a pump-driven host "fire and let the scheduler drive it"
  /// is exactly the intended pattern.
  ///
  /// To block the calling thread until a script finishes (e.g. in a tool or
  /// test that has no game loop), pass this value to
  /// <see cref="IScriptScheduler.RunUntilComplete"/>.
  /// </summary>
  public readonly struct ScriptEvaluation
  {
    internal Task Task { get; }

    internal ScriptEvaluation(Task task) => Task = task;

    /// <summary>
    /// <c>true</c> once the script has run to completion (or faulted/been
    /// cancelled). A game-loop host can check this after each
    /// <see cref="IScriptLoop.Pump"/> call to detect that no further work
    /// remains.
    /// </summary>
    public bool IsCompleted => Task.IsCompleted;
  }
}
