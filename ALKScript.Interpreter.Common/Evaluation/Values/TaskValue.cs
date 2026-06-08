using System.Threading.Tasks;

namespace ALKScript.Interpreter.Common.Evaluation.Values
{
  /// <summary>
  /// An in-flight or completed asynchronous operation, as seen from ALKScript:
  /// the value an <c>async</c> function call or an <c>async native</c>
  /// invocation produces, and the thing an <c>await</c> expression unwraps.
  ///
  /// Wraps a real <see cref="System.Threading.Tasks.Task{TResult}"/> so that
  /// "real suspension" — an <c>await</c> that genuinely parks the calling
  /// script's evaluation until the operation settles — falls directly out of
  /// the Task-based evaluator spine built in Phase 2: awaiting this value's
  /// <see cref="Task"/> suspends the compiler-generated continuation chain
  /// exactly like awaiting any other <see cref="System.Threading.Tasks.Task"/>,
  /// and resumes it (with the produced value, or a converted fault) once the
  /// underlying operation settles.
  /// </summary>
  public sealed class TaskValue : ALKScriptValue
  {
    /// <summary>The underlying task. May already be completed (e.g. a native that resolved synchronously) or still pending.</summary>
    public Task<ALKScriptValue> Task { get; }

    public TaskValue(Task<ALKScriptValue> task)
    {
      Task = task;
    }

    /// <summary>An already-completed task wrapping <paramref name="value"/> — e.g. for natives/results that resolve synchronously.</summary>
    public static TaskValue FromResult(ALKScriptValue value) => new TaskValue(System.Threading.Tasks.Task.FromResult(value));

    public override string TypeName => "Task";

    public override string ToString() => $"<task {(Task.IsCompleted ? "completed" : "pending")}>";
  }
}
