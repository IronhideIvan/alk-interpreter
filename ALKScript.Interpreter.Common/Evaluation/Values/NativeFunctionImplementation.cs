using System.Collections.Generic;

namespace ALKScript.Interpreter.Common.Evaluation.Values
{
  /// <summary>
  /// Implements the body of a <c>native</c> declaration: invoked directly by
  /// the host runtime with already-evaluated argument values.
  /// </summary>
  public delegate ALKScriptValue NativeFunctionImplementation(IReadOnlyList<ALKScriptValue> arguments);
}
