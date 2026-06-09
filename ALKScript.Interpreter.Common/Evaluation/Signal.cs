using ALKScript.Interpreter.Common.Evaluation.Values;

namespace ALKScript.Interpreter.Common.Evaluation
{
  /// <summary>
  /// A pending non-local exit from normal statement execution: either a
  /// "return" unwinding to the nearest enclosing function/constructor call,
  /// or a "throw" unwinding to the nearest enclosing "try"/"catch" (or the
  /// top level). Carried as evaluator state — see <c>ProgramEvaluator._signal</c>
  /// — rather than as a .NET exception, so that scripts that return early or
  /// throw values do not rely on .NET's exception machinery for what is, at
  /// the script level, ordinary control flow.
  /// </summary>
  public readonly struct Signal
  {
    public SignalKind Kind { get; }
    public ALKScriptValue Value { get; }

    private Signal(SignalKind kind, ALKScriptValue value)
    {
      Kind = kind;
      Value = value;
    }

    public static Signal Return(ALKScriptValue value) => new Signal(SignalKind.Return, value);

    public static Signal Thrown(ALKScriptValue value) => new Signal(SignalKind.Thrown, value);

    public static Signal Break() => new Signal(SignalKind.Break, NullValue.Instance);

    public static Signal Continue() => new Signal(SignalKind.Continue, NullValue.Instance);

    /// <summary>
    /// Creates a "Cancelled" signal — an uncatchable unwind of the entire
    /// script raised in response to an external stop request. It carries no
    /// script-meaningful payload (scripts cannot observe or react to it; only
    /// "finally" blocks run on the way out), so its <see cref="Value"/> is
    /// always <see cref="NullValue.Instance"/>.
    /// </summary>
    public static Signal Cancelled() => new Signal(SignalKind.Cancelled, NullValue.Instance);
  }
}
