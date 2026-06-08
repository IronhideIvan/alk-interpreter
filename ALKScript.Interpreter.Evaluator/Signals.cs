using System;
using ALKScript.Interpreter.Common.Evaluation.Values;

namespace ALKScript.Interpreter.Evaluator
{
  /// <summary>
  /// Internal control-flow signal used to unwind the call stack on a "return"
  /// statement. Not a user-visible error — caught by the function-call machinery.
  /// </summary>
  internal sealed class ReturnSignal : Exception
  {
    public ALKScriptValue Value { get; }

    public ReturnSignal(ALKScriptValue value)
    {
      Value = value;
    }
  }

  /// <summary>
  /// Internal control-flow signal carrying a thrown ALKScript value, used to
  /// unwind to the nearest enclosing "catch"/"finally". Not a user-visible
  /// .NET error in its own right — <see cref="Value"/> is the ALKScript-level payload.
  /// </summary>
  internal sealed class ThrownValueSignal : Exception
  {
    public ALKScriptValue Value { get; }

    public ThrownValueSignal(ALKScriptValue value)
    {
      Value = value;
    }
  }
}
