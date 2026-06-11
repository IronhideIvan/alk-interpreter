using System.Threading.Tasks;
using ALKScript.Interpreter.Common.Ast;

namespace ALKScript.Interpreter.Common.Evaluation.Values
{
  /// <summary>
  /// An in-flight or completed asynchronous operation, as seen from ALKScript:
  /// the value a <c>thunk</c>/<c>thunk&lt;T&gt;</c>-typed native call produces,
  /// and the thing an <c>await</c> expression unwraps.
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
  public sealed class ThunkValue : ALKScriptValue
  {
    /// <summary>The underlying task. May already be completed (e.g. a native that resolved synchronously) or still pending.</summary>
    public Task<ALKScriptValue> Task { get; }

    /// <summary>
    /// The "T" of the declared <c>thunk&lt;T&gt;</c> return type that produced
    /// this value, if any — used to validate the resolved result on
    /// <c>await</c>. <c>null</c> for a bare <c>thunk</c> (nothing to validate
    /// against).
    /// </summary>
    public TypeNode? ElementType { get; }

    public ThunkValue(Task<ALKScriptValue> task, TypeNode? elementType = null)
    {
      Task = task;
      ElementType = elementType;
    }

    /// <summary>An already-completed task wrapping <paramref name="value"/> — e.g. for natives/results that resolve synchronously.</summary>
    public static ThunkValue FromResult(ALKScriptValue value, TypeNode? elementType = null) => new ThunkValue(System.Threading.Tasks.Task.FromResult(value), elementType);

    /// <summary>Returns a copy of this value, wrapping the same <see cref="Task"/>, tagged with <paramref name="elementType"/>.</summary>
    public ThunkValue WithElementType(TypeNode? elementType) => new ThunkValue(Task, elementType);

    public override string TypeName => "thunk";

    public override string ToString() => $"<task {(Task.IsCompleted ? "completed" : "pending")}>";
  }
}
