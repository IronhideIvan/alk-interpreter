namespace ALKScript.Interpreter.Common.Evaluation.Scheduling
{
  /// <summary>The outcome of <c>CursorProgramEvaluator.Evaluate</c>/<c>Resume</c>/<c>ResumeFaulted</c>, and of <see cref="IProgramRun.Result"/>.</summary>
  public enum ProgramRunResult
  {
    Completed,
    Awaiting,
  }
}
