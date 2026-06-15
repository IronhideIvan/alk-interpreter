using ALKScript.Interpreter.Common.Ast;

namespace ALKScript.Interpreter.Common.Evaluation.Values
{
  /// <summary>
  /// An already-settled <c>thunk</c>/<c>thunk&lt;T&gt;</c> value, as seen from
  /// ALKScript: the value a synchronous-but-<c>thunk</c>-typed native call
  /// produces, or the result an <c>await</c> expression unwraps once a
  /// <see cref="PendingOperationValue"/> has resolved.
  ///
  /// Always wraps an already-available <see cref="Result"/> — there is no
  /// "live" or "pending" <see cref="ThunkValue"/>. A native operation that can
  /// genuinely be pending must be declared <c>native async</c> and go through
  /// <see cref="PendingOperation"/> + <see cref="ALKScript.Interpreter.Common.Evaluation.Scheduling.IAsyncOperationBinder"/>
  /// (see <see cref="PendingOperationValue"/>) — the single path for pending
  /// state.
  /// </summary>
  public sealed class ThunkValue : ALKScriptValue
  {
    /// <summary>The settled value.</summary>
    public ALKScriptValue Result { get; }

    /// <summary>
    /// The "T" of the declared <c>thunk&lt;T&gt;</c> return type that produced
    /// this value, if any — used to validate <see cref="Result"/> on
    /// <c>await</c>. <c>null</c> for a bare <c>thunk</c> (nothing to validate
    /// against).
    /// </summary>
    public TypeNode? ElementType { get; }

    public ThunkValue(ALKScriptValue result, TypeNode? elementType = null)
    {
      Result = result;
      ElementType = elementType;
    }

    /// <summary>Thin factory for call-site readability — equivalent to the constructor.</summary>
    public static ThunkValue FromResult(ALKScriptValue value, TypeNode? elementType = null) => new ThunkValue(value, elementType);

    /// <summary>Returns a copy of this value, wrapping the same <see cref="Result"/>, tagged with <paramref name="elementType"/>.</summary>
    public ThunkValue WithElementType(TypeNode? elementType) => new ThunkValue(Result, elementType);

    public override string TypeName => "thunk";

    public override string ToString() => "<thunk resolved>";
  }
}
