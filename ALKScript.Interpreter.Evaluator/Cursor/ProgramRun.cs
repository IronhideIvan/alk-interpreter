using System;
using ALKScript.Interpreter.Common.Evaluation.Scheduling;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Common.Modules;

namespace ALKScript.Interpreter.Evaluator.Cursor
{
  /// <summary>
  /// A single in-progress (or completed) execution of a <see cref="ModuleGraph"/>
  /// via a <see cref="CursorProgramEvaluator"/>. Wraps the evaluator instance
  /// together with its most recent <see cref="ProgramRunResult"/>, giving hosts
  /// and tests one object to hold onto and drive to completion.
  /// </summary>
  public sealed class ProgramRun
  {
    public CursorProgramEvaluator Evaluator { get; }

    public ProgramRunResult Result { get; private set; }

    /// <summary>What <see cref="Resume"/>/<see cref="ResumeFaulted"/> will settle, while <see cref="Result"/> is <see cref="ProgramRunResult.Awaiting"/>.</summary>
    public AwaitHandle? PendingAwait => Evaluator.PendingAwait;

    /// <inheritdoc cref="CursorProgramEvaluator.Log"/>
    public System.Collections.Generic.IReadOnlyList<OperationLogEntry> Log => Evaluator.Log;

    private ProgramRun(CursorProgramEvaluator evaluator, ProgramRunResult result)
    {
      Evaluator = evaluator;
      Result = result;
    }

    /// <summary>Begins evaluating <paramref name="graph"/> with <paramref name="evaluator"/>.</summary>
    public static ProgramRun Start(CursorProgramEvaluator evaluator, ModuleGraph graph) =>
      new ProgramRun(evaluator, evaluator.Evaluate(graph));

    /// <summary>Resumes a suspended run with the settled result of the pending <c>await</c>.</summary>
    public ProgramRunResult Resume(ALKScriptValue value)
    {
      Result = Evaluator.Resume(value);
      return Result;
    }

    /// <summary>Resumes a suspended run by raising <paramref name="faultMessage"/> as a thrown exception at the point of suspension.</summary>
    public ProgramRunResult ResumeFaulted(string faultMessage)
    {
      Result = Evaluator.ResumeFaulted(faultMessage);
      return Result;
    }

    /// <summary>
    /// Blocks, driving this run from its current state to
    /// <see cref="ProgramRunResult.Completed"/> by synchronously waiting on
    /// each <see cref="PendingAwait"/> in turn and resuming with its settled
    /// result. A composite <c>await [a, b, c]</c> (<see cref="AwaitHandle.CompositeTask"/>)
    /// swallows per-element faults — matching <c>await [...]</c>'s own
    /// semantics, where faults surface only if the script re-awaits the
    /// faulted element — and resumes with <see cref="NullValue.Instance"/>.
    /// A single-element <c>await</c> resumes with the task's result, or calls
    /// <see cref="ResumeFaulted"/> with the exception's message on fault.
    ///
    /// <paramref name="binder"/> is required only if a pending <c>await</c> is
    /// parked on a not-yet-started <see cref="PendingOperation"/>
    /// (<see cref="AwaitHandle.Operation"/> set with <see cref="AwaitHandle.Task"/>
    /// null) — in practice the cursor evaluator always starts the operation
    /// before suspending, so this is a defensive fallback.
    /// </summary>
    public void RunToCompletion(IAsyncOperationBinder? binder = null)
    {
      while (Result == ProgramRunResult.Awaiting)
      {
        var handle = PendingAwait!;

        if (handle.CompositeTask != null)
        {
          try { handle.CompositeTask.GetAwaiter().GetResult(); }
          catch { /* per-element faults are swallowed, matching await [...] semantics */ }

          Resume(NullValue.Instance);
          continue;
        }

        var task = handle.Task ?? binder?.Start(handle.Operation!)
          ?? throw new InvalidOperationException("ProgramRun.RunToCompletion: pending await has no Task and no IAsyncOperationBinder was supplied to start its Operation.");

        try
        {
          var value = task.GetAwaiter().GetResult();
          Resume(value);
        }
        catch (Exception ex)
        {
          ResumeFaulted(ex.Message);
        }
      }
    }
  }
}
