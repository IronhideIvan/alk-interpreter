using System.Collections.Generic;
using ALKScript.Interpreter.Common.Evaluation.Values;

namespace ALKScript.Interpreter.Common.Evaluation.Scheduling
{
  /// <summary>
  /// A single in-progress (or completed) execution of an ALKScript program,
  /// as seen by a host or test. Implemented by <c>ProgramRun</c>
  /// (<c>ALKScript.Interpreter.Evaluator.Cursor</c>); declared here so hosts
  /// and tests can depend on the run's public surface without referencing the
  /// evaluator project, and can substitute a mock/fake implementation.
  /// </summary>
  public interface IProgramRun
  {
    /// <summary>The outcome of the most recent <see cref="Resume"/>/<see cref="ResumeFaulted"/>/<see cref="Pump"/> (or the initial run).</summary>
    ProgramRunResult Result { get; }

    /// <summary>What <see cref="Resume"/>/<see cref="ResumeFaulted"/> will settle, while <see cref="Result"/> is <see cref="ProgramRunResult.Awaiting"/>.</summary>
    AwaitHandle? PendingAwait { get; }

    /// <summary>The record-and-replay log accumulated so far.</summary>
    IReadOnlyList<OperationLogEntry> Log { get; }

    /// <summary>Resumes a suspended run with the settled result of the pending <c>await</c>.</summary>
    ProgramRunResult Resume(ALKScriptValue value);

    /// <summary>Resumes a suspended run by raising <paramref name="faultMessage"/> as a thrown exception at the point of suspension.</summary>
    ProgramRunResult ResumeFaulted(string faultMessage);

    /// <summary>
    /// Polls the current <see cref="PendingAwait"/> (if any) and advances the
    /// run if it has settled. A no-op (returns <see cref="Result"/> unchanged)
    /// if not currently <see cref="ProgramRunResult.Awaiting"/>, or if the
    /// pending operation(s) are still <see cref="OperationStatus.Pending"/>.
    /// Safe to call repeatedly from a host's game loop / pump.
    /// </summary>
    ProgramRunResult Pump();

    /// <summary>
    /// Repeatedly calls <see cref="Pump"/> until the run leaves the
    /// <see cref="ProgramRunResult.Awaiting"/> state. Suitable for tests /
    /// synchronous binders whose <c>IAsyncOperationBinder.Start</c> never
    /// returns <see cref="OperationStatus.Pending"/> (they block internally).
    /// </summary>
    void RunToCompletion();
  }
}
