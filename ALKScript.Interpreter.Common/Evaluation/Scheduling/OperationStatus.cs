using System;
using ALKScript.Interpreter.Common.Evaluation.Values;

namespace ALKScript.Interpreter.Common.Evaluation.Scheduling
{
  /// <summary>
  /// The tri-state outcome of a host-side <see cref="PendingOperation"/>, as
  /// reported by <see cref="IAsyncOperationBinder.Start"/>/<see cref="IAsyncOperationBinder.Poll"/>.
  /// Replaces <see cref="System.Threading.Tasks.Task{TResult}"/> at the
  /// evaluator/host boundary: the evaluator never blocks on or observes a
  /// <c>Task</c> — it only ever sees one of these three states.
  /// </summary>
  public abstract class OperationStatus
  {
    private OperationStatus() { }

    /// <summary>The operation has not yet settled; the host should report progress later via <see cref="IAsyncOperationBinder.Poll"/>.</summary>
    public sealed class Pending : OperationStatus
    {
      public static readonly Pending Instance = new();
    }

    /// <summary>The operation settled successfully with <see cref="Value"/>.</summary>
    public sealed class Resolved : OperationStatus
    {
      public ALKScriptValue Value { get; }

      public Resolved(ALKScriptValue value) => Value = value;
    }

    /// <summary>The operation settled with <see cref="Error"/>.</summary>
    public sealed class Faulted : OperationStatus
    {
      public Exception Error { get; }

      public Faulted(Exception error) => Error = error;
    }
  }
}
