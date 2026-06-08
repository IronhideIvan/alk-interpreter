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
}
