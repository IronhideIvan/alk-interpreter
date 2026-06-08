using System.Collections.Generic;

namespace ALKScript.Interpreter.Common.Evaluation.Values
{
  /// <summary>
  /// Implements the body of a <c>native</c> method declaration: invoked
  /// directly by the host runtime with the receiving instance — so the
  /// implementation can read and mutate <see cref="InstanceValue.Fields"/>,
  /// e.g. to back a collection class with a real, host-managed data
  /// structure — plus already-evaluated argument values.
  ///
  /// Distinct from <see cref="NativeFunctionImplementation"/> (which has no
  /// receiver) because free-standing <c>native function</c>s and <c>native</c>
  /// methods resolve against separate host-binding tables — see
  /// <see cref="ScriptNativeBindings"/> and <see cref="ScriptNativeMethodBindings"/>.
  /// </summary>
  public delegate ALKScriptValue NativeMethodImplementation(InstanceValue instance, IReadOnlyList<ALKScriptValue> arguments);
}
