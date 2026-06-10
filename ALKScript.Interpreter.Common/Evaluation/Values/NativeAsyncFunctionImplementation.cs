using System.Collections.Generic;
using System.Threading.Tasks;

namespace ALKScript.Interpreter.Common.Evaluation.Values
{
  /// <summary>
  /// Implements the body of a host-provided function that itself needs to call
  /// back into script-defined callables (e.g. invoking a <c>lambda&lt;...&gt;</c>
  /// argument via <see cref="IEvaluationContext"/>'s <c>Call</c>), and therefore
  /// must be <see cref="Task"/>-returning rather than synchronous like
  /// <see cref="NativeFunctionImplementation"/>.
  /// </summary>
  public delegate Task<ALKScriptValue> NativeAsyncFunctionImplementation(IReadOnlyList<ALKScriptValue> arguments);
}
