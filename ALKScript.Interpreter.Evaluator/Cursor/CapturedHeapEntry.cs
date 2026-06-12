using System.Collections.Generic;
using ALKScript.Interpreter.Common.Ast;

namespace ALKScript.Interpreter.Evaluator.Cursor
{
  /// <summary>
  /// One entry of <see cref="CursorStructuralCaptureState.Heap"/> — a heap
  /// object reachable from the suspended trail/environments, addressed by
  /// its index (a <see cref="CapturedHeapValue.HeapRef"/>) rather than
  /// stored inline, so cyclic object graphs (<c>a.next = b; b.next = a;</c>)
  /// round-trip correctly (docs/ASYNC_AWAIT_DESIGN.md Addendum 3, Step 9).
  /// </summary>
  public abstract class CapturedHeapEntry
  {
    /// <summary>An <c>InstanceValue</c> — a class's declaration address plus its field storage and generic type arguments.</summary>
    public sealed class Instance : CapturedHeapEntry
    {
      public AstReference ClassRef { get; }

      public Dictionary<string, CapturedHeapValue> Fields { get; }

      public IReadOnlyDictionary<string, TypeNode> TypeArguments { get; }

      public Instance(AstReference classRef, Dictionary<string, CapturedHeapValue> fields, IReadOnlyDictionary<string, TypeNode> typeArguments)
      {
        ClassRef = classRef;
        Fields = fields;
        TypeArguments = typeArguments;
      }
    }

    /// <summary>A <c>BaseValue</c> — a superclass declaration address paired with the heap id of the <c>this</c> instance it wraps.</summary>
    public sealed class Base : CapturedHeapEntry
    {
      public AstReference SuperclassRef { get; }

      public int InstanceId { get; }

      public Base(AstReference superclassRef, int instanceId)
      {
        SuperclassRef = superclassRef;
        InstanceId = instanceId;
      }
    }
  }
}
