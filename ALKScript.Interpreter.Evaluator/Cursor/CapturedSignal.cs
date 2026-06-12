using ALKScript.Interpreter.Common.Evaluation;

namespace ALKScript.Interpreter.Evaluator.Cursor
{
  /// <summary>
  /// A captured <see cref="Signal"/> (return/throw/break/continue/cancelled)
  /// — part of the "Phase B" structural Capture/Restore design
  /// (docs/ASYNC_AWAIT_DESIGN.md Addendum 3). <see cref="Value"/> is always
  /// present (a <see cref="ALKScript.Interpreter.Common.Evaluation.Values.NullValue"/>
  /// for break/continue/cancelled, mirroring <see cref="Signal"/> itself).
  /// </summary>
  public sealed class CapturedSignal
  {
    public SignalKind Kind { get; set; }

    public CapturedHeapValue Value { get; set; } = null!;

    public static CapturedSignal From(Signal signal) => new CapturedSignal
    {
      Kind = signal.Kind,
      Value = CapturedHeapValue.FromPrimitive(signal.Value),
    };

    public Signal ToSignal()
    {
      var value = ((CapturedHeapValue.Primitive)Value).Value;

      return Kind switch
      {
        SignalKind.Return => Signal.Return(value),
        SignalKind.Thrown => Signal.Thrown(value),
        SignalKind.Break => Signal.Break(),
        SignalKind.Continue => Signal.Continue(),
        SignalKind.Cancelled => Signal.Cancelled(),
        _ => throw new System.FormatException($"Unknown captured signal kind '{Kind}'."),
      };
    }
  }
}
