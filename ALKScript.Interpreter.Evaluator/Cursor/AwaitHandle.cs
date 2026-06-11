using System.Threading.Tasks;
using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Common.Evaluation.Scheduling;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Evaluator.Cursor
{
  /// <summary>
  /// Identifies what a suspended <see cref="EvaluationCursor"/> is parked on
  /// when <see cref="StepResult.IsAwaiting"/> is true.
  ///
  /// Exactly one of <see cref="Operation"/> or <see cref="Task"/> is set:
  /// <see cref="Operation"/> for a not-yet-started <c>async native</c>
  /// (<see cref="PendingOperationValue"/>); <see cref="Task"/> for an
  /// already-started/completed operation (<see cref="ThunkValue"/>, or a
  /// <see cref="PendingOperationValue"/> once <see cref="PendingOperationValue.Start"/>
  /// has been called). <see cref="ElementType"/> carries the declared
  /// <c>thunk&lt;T&gt;</c> element type through to <see cref="EvaluationCursor.Resume"/>
  /// for <see cref="TypeChecking.MatchesType"/> validation.
  /// </summary>
  internal sealed class AwaitHandle
  {
    public PendingOperation? Operation { get; }

    public Task<ALKScriptValue>? Task { get; }

    public TypeNode? ElementType { get; }

    /// <summary>The <c>await</c> keyword token, used to report a type mismatch on <see cref="EvaluationCursor.Resume"/>.</summary>
    public ALKScriptToken Site { get; }

    private AwaitHandle(PendingOperation? operation, Task<ALKScriptValue>? task, TypeNode? elementType, ALKScriptToken site)
    {
      Operation = operation;
      Task = task;
      ElementType = elementType;
      Site = site;
    }

    public static AwaitHandle ForTask(Task<ALKScriptValue> task, TypeNode? elementType, ALKScriptToken site) =>
      new AwaitHandle(operation: null, task, elementType, site);

    public static AwaitHandle ForOperation(PendingOperation operation, TypeNode? elementType, ALKScriptToken site) =>
      new AwaitHandle(operation, task: null, elementType, site);

    /// <summary>
    /// A <see cref="PendingOperationValue"/> whose <see cref="PendingOperationValue.Start"/>
    /// task did not complete synchronously — carries both <see cref="Task"/>
    /// (for the host to observe) and <see cref="Operation"/> (so
    /// <see cref="EvaluationCursor.Resume"/>/<see cref="EvaluationCursor.ResumeFaulted"/>
    /// can append the settled outcome to the replay log).
    /// </summary>
    public static AwaitHandle ForPendingTask(Task<ALKScriptValue> task, PendingOperation operation, TypeNode? elementType, ALKScriptToken site) =>
      new AwaitHandle(operation, task, elementType, site);
  }
}
