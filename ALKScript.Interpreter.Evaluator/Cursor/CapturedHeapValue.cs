using System;
using ALKScript.Interpreter.Common.Evaluation.Values;

namespace ALKScript.Interpreter.Evaluator.Cursor
{
  /// <summary>
  /// A captured reference to an <see cref="ALKScriptValue"/> — part of the
  /// "Phase B" structural Capture/Restore design (docs/ASYNC_AWAIT_DESIGN.md
  /// Addendum 3). <see cref="Primitive"/> is the only variant implemented so
  /// far (Step 6): an int/float/string/bool/null/array value, stored directly
  /// (no further reference table needed). Later steps add variants for heap
  /// objects (<c>InstanceValue</c>/<c>NamespaceValue</c>/<c>BaseValue</c>, by
  /// heap id) and AST-addressable values (<c>ClassValue</c>/<c>FunctionValue</c>/
  /// etc., by <see cref="AstReference"/>).
  /// </summary>
  public abstract class CapturedHeapValue
  {
    /// <summary>An int/float/string/bool/null value, or an array of such values (recursively).</summary>
    public sealed class Primitive : CapturedHeapValue
    {
      public ALKScriptValue Value { get; }

      public Primitive(ALKScriptValue value)
      {
        Value = value;
      }
    }

    /// <summary>
    /// A reference to a top-level <c>ClassValue</c>/<c>InterfaceValue</c>/
    /// <c>EnumTypeValue</c>/free-standing <c>FunctionValue</c> — addressed by
    /// <see cref="AstReference"/> rather than stored inline, since
    /// <see cref="CursorProgramEvaluator.RestoreStructural"/>'s
    /// declaration-prefix run already re-creates exactly one instance of each
    /// (Step 8). Bound methods, lambdas, enum members, and namespace values
    /// are not yet supported (Steps 9-11).
    /// </summary>
    public sealed class AstRef : CapturedHeapValue
    {
      public AstReference Reference { get; }

      public AstRef(AstReference reference)
      {
        Reference = reference;
      }
    }

    /// <summary>
    /// A reference to a <see cref="CursorStructuralCaptureState.Heap"/> entry —
    /// an <c>InstanceValue</c>/<c>BaseValue</c> reachable from the suspended
    /// trail/environments, addressed by index so cyclic object graphs
    /// round-trip correctly (Step 9).
    /// </summary>
    public sealed class HeapRef : CapturedHeapValue
    {
      public int Id { get; }

      public HeapRef(int id)
      {
        Id = id;
      }
    }

    /// <summary>
    /// A bound method value (<c>obj.method</c>) — the method's declaration,
    /// addressed via <see cref="AstReference"/> as
    /// <c>"&lt;ClassName&gt;.&lt;methodName&gt;"</c> (see
    /// <see cref="AstResolver.AddressOfMember"/>), paired with a
    /// <see cref="HeapRef"/> to the bound <c>InstanceValue</c> (Step 11).
    /// Free-standing functions and lambdas are addressed via <see cref="AstRef"/>
    /// and are not yet supported, respectively.
    /// </summary>
    public sealed class Method : CapturedHeapValue
    {
      public AstReference Reference { get; }

      public HeapRef Instance { get; }

      public Method(AstReference reference, HeapRef instance)
      {
        Reference = reference;
        Instance = instance;
      }
    }

    /// <summary>
    /// A bound <em>native</em> method value (<c>obj.nativeMethod</c>) — same
    /// shape as <see cref="Method"/>, but resolved on Restore via
    /// <see cref="FunctionValueFactory.CreateMethod"/>'s native branch rather
    /// than reconstructing a <c>FunctionValue</c> (Step 13 follow-up,
    /// docs/ASYNC_AWAIT_DESIGN.md Addendum 3).
    /// </summary>
    public sealed class NativeMethod : CapturedHeapValue
    {
      public AstReference Reference { get; }

      public HeapRef Instance { get; }

      public NativeMethod(AstReference reference, HeapRef instance)
      {
        Reference = reference;
        Instance = instance;
      }
    }

    /// <summary>
    /// A reference to a <see cref="CursorStructuralCaptureState.PendingOperations"/>
    /// entry — a <c>PendingOperationValue</c>/<c>ThunkValue</c> held in a local
    /// variable, addressed by index so the same instance referenced from
    /// multiple places (e.g. a local <c>op</c> and the suspending
    /// <c>await</c>'s own operand) round-trips as a single reconstructed
    /// instance ("Phase C", docs/ASYNC_AWAIT_DESIGN.md Addendum 3).
    /// </summary>
    public sealed class PendingOpRef : CapturedHeapValue
    {
      public int Id { get; }

      public PendingOpRef(int id)
      {
        Id = id;
      }
    }

    /// <summary>
    /// Wraps <paramref name="value"/> as a <see cref="Primitive"/>, throwing
    /// <see cref="NotSupportedException"/> if it (or, for an array, any of its
    /// elements, recursively) is not an int/float/string/bool/null/array value.
    /// </summary>
    public static Primitive FromPrimitive(ALKScriptValue value)
    {
      if (!TryFromPrimitive(value, out var primitive))
      {
        throw new NotSupportedException(
          $"Cannot capture a value of runtime type '{value.GetType().Name}' (ALKScript type '{value.TypeName}') — " +
          "only int/float/string/bool/null/array values are supported by the structural Capture/Restore " +
          "design's current milestone (docs/ASYNC_AWAIT_DESIGN.md Addendum 3).");
      }

      return primitive!;
    }

    /// <summary>
    /// As <see cref="FromPrimitive"/>, but returns <c>false</c> instead of
    /// throwing if <paramref name="value"/> (or, for an array, any of its
    /// elements, recursively) is not an int/float/string/bool/null/array value.
    /// </summary>
    public static bool TryFromPrimitive(ALKScriptValue value, out Primitive? primitive)
    {
      switch (value)
      {
        case IntValue:
        case FloatValue:
        case StringValue:
        case BoolValue:
        case NullValue:
          primitive = new Primitive(value);
          return true;

        case ArrayValue arrayValue:
          foreach (var item in arrayValue.Items)
          {
            if (!TryFromPrimitive(item, out _))
            {
              primitive = null;
              return false;
            }
          }

          primitive = new Primitive(value);
          return true;

        default:
          primitive = null;
          return false;
      }
    }
  }
}
