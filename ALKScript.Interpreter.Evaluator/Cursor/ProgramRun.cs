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
  public sealed class ProgramRun : IProgramRun
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

    /// <summary>
    /// Wraps a previously-restored <paramref name="evaluator"/> (e.g. via
    /// <c>CursorStructuralStateSerializer.Restore</c>) and its reported
    /// <paramref name="result"/> in a <see cref="ProgramRun"/>.
    /// </summary>
    public static ProgramRun Restore(CursorProgramEvaluator evaluator, ProgramRunResult result) =>
      new ProgramRun(evaluator, result);

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
    /// Polls the current <see cref="PendingAwait"/> (if any) and advances the
    /// run if it has settled. A no-op (returns <see cref="Result"/> unchanged)
    /// if not currently <see cref="ProgramRunResult.Awaiting"/>, or if the
    /// pending operation(s) are still <see cref="OperationStatus.Pending"/>.
    /// Safe to call repeatedly from a host's game loop / pump — each call
    /// polls every still-pending <see cref="PendingOperationValue"/> via
    /// <see cref="IAsyncOperationBinder.Poll"/> exactly once.
    /// </summary>
    public ProgramRunResult Pump()
    {
      if (Result != ProgramRunResult.Awaiting) return Result;

      var handle = PendingAwait!;

      if (handle.CompositeElements != null)
      {
        var allSettled = true;
        foreach (var element in handle.CompositeElements)
        {
          if (element.Source is PendingOperationValue pending && pending.Poll() is OperationStatus.Pending)
          {
            allSettled = false;
          }
        }

        if (allSettled) Resume(NullValue.Instance);
        return Result;
      }

      var pendingOperation = (PendingOperationValue)handle.Source!;
      switch (pendingOperation.Poll())
      {
        case OperationStatus.Resolved resolved:
          Resume(resolved.Value);
          break;

        case OperationStatus.Faulted faulted:
          ResumeFaulted(faulted.Error.Message);
          break;

          // Pending: leave Result == Awaiting.
      }

      return Result;
    }

    /// <summary>
    /// Repeatedly calls <see cref="Pump"/> until the run leaves the
    /// <see cref="ProgramRunResult.Awaiting"/> state, sleeping briefly between
    /// calls that made no progress. Suitable for tests / synchronous binders
    /// whose <see cref="IAsyncOperationBinder.Start"/> never returns
    /// <see cref="OperationStatus.Pending"/> (they block internally). For
    /// genuinely-async binders this busy-polls — hosts with real async work
    /// should call <see cref="Pump"/> from their own loop instead.
    /// </summary>
    public void RunToCompletion()
    {
      while (Result == ProgramRunResult.Awaiting)
      {
        if (Pump() == ProgramRunResult.Awaiting)
        {
          System.Threading.Thread.Sleep(10);
        }
      }
    }
  }
}
