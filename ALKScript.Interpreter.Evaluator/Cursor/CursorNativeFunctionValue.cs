using System.Collections.Generic;
using ALKScript.Interpreter.Common.Evaluation.Values;

namespace ALKScript.Interpreter.Evaluator.Cursor
{
  /// <summary>
  /// A function value backed by a host-provided implementation that needs to
  /// call back into the cursor evaluator (e.g. <c>array.map(callback)</c>
  /// invoking <c>callback</c> for each element). Cursor-evaluator counterpart
  /// to <see cref="NativeAsyncFunctionValue"/> — the implementation runs
  /// synchronously against an <see cref="EvaluationCursor"/> and returns a
  /// <see cref="StepResult"/>.
  /// </summary>
  internal delegate StepResult CursorNativeFunctionImplementation(IReadOnlyList<ALKScriptValue> arguments, EvaluationCursor cursor);

  internal sealed class CursorNativeFunctionValue : CallableValue
  {
    public string Name { get; }
    public CursorNativeFunctionImplementation Implementation { get; }

    private readonly int _arity;

    public CursorNativeFunctionValue(string name, int arity, CursorNativeFunctionImplementation implementation)
    {
      Name = name;
      _arity = arity;
      Implementation = implementation;
    }

    public override int Arity => _arity;

    public override string TypeName => "function";

    public override string ToString() => $"<native function {Name}>";
  }
}
