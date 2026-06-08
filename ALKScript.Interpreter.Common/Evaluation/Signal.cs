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
  }
}
