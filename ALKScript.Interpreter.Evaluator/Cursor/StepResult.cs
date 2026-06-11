using ALKScript.Interpreter.Common.Evaluation.Values;

namespace ALKScript.Interpreter.Evaluator.Cursor
{
  /// <summary>
  /// The result of a single step of <see cref="EvaluationCursor"/>'s
  /// synchronous walk over the AST. Replaces <c>Task&lt;ALKScriptValue&gt;</c>
  /// (and <c>Task</c> for statements) as the return type of every
  /// cursor-evaluator method.
  ///
  /// Either the step ran to completion with <see cref="Value"/> set, or it
  /// hit an unresolved <c>await</c> and <see cref="Handle"/> describes what
  /// it's now parked on — in which case callers must propagate the
  /// <see cref="Awaiting(AwaitHandle)"/> result upward unchanged rather than
  /// reading <see cref="Value"/>.
  /// </summary>
  internal readonly struct StepResult
  {
    public bool IsAwaiting { get; }

    public ALKScriptValue? Value { get; }

    public AwaitHandle? Handle { get; }

    private StepResult(bool isAwaiting, ALKScriptValue? value, AwaitHandle? handle)
    {
      IsAwaiting = isAwaiting;
      Value = value;
      Handle = handle;
    }

    public static StepResult Completed(ALKScriptValue value) => new StepResult(false, value, null);

    public static StepResult Awaiting(AwaitHandle handle) => new StepResult(true, null, handle);
  }
}
