using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Common.Evaluation.Scheduling;

namespace ALKScript.Interpreter.Evaluator.Cursor
{
  /// <summary>
  /// One captured element of a composite <c>await [a, b, c]</c> suspension
  /// (see <see cref="AwaitElement"/>/<see cref="AwaitHandle.ForComposite"/>) —
  /// part of the "Phase B" structural Capture/Restore design
  /// (docs/ASYNC_AWAIT_DESIGN.md Addendum 3, Step 12).
  /// </summary>
  public abstract class CapturedAwaitElement
  {
    /// <summary>An element that already had a value — either a plain operand or a settled (successful) live operation.</summary>
    public sealed class Resolved : CapturedAwaitElement
    {
      public CapturedHeapValue Value { get; }

      public TypeNode? ElementType { get; }

      public Resolved(CapturedHeapValue value, TypeNode? elementType)
      {
        Value = value;
        ElementType = elementType;
      }
    }

    /// <summary>A live operation that had not yet settled — restarted via <see cref="IAsyncOperationBinder.Start"/> on Restore.</summary>
    public sealed class Reissue : CapturedAwaitElement
    {
      public PendingOperation Operation { get; }

      public TypeNode? ElementType { get; }

      public Reissue(PendingOperation operation, TypeNode? elementType)
      {
        Operation = operation;
        ElementType = elementType;
      }
    }

    /// <summary>
    /// An element whose underlying <see cref="ALKScript.Interpreter.Common.Evaluation.Values.PendingOperationValue"/>/
    /// <see cref="ALKScript.Interpreter.Common.Evaluation.Values.ThunkValue"/> instance is also referenced from a
    /// local variable — points into <see cref="CursorStructuralCaptureState.PendingOperations"/> for dedup
    /// (docs/ASYNC_AWAIT_DESIGN.md Addendum 3, "Phase C" composite-aliasing).
    /// </summary>
    public sealed class OperationRef : CapturedAwaitElement
    {
      public int Id { get; }

      public TypeNode? ElementType { get; }

      public OperationRef(int id, TypeNode? elementType)
      {
        Id = id;
        ElementType = elementType;
      }
    }

    /// <summary>An element whose outcome was a fault.</summary>
    public sealed class Fault : CapturedAwaitElement
    {
      public string Message { get; }

      public TypeNode? ElementType { get; }

      public Fault(string message, TypeNode? elementType)
      {
        Message = message;
        ElementType = elementType;
      }
    }
  }
}
