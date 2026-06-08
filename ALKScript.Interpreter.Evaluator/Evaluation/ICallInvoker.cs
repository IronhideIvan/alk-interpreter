using System.Collections.Generic;
using System.Threading.Tasks;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Evaluator
{
  /// <summary>
  /// Resolves a callee to an invocation, binds arguments to parameters, and
  /// runs constructors for <c>new</c> expressions. <see cref="Task"/>-returning
  /// so that an <c>await</c> within a function/constructor body can suspend the
  /// call mid-execution and resume it later.
  /// </summary>
  internal interface ICallInvoker
  {
    Task<ALKScriptValue> Call(ALKScriptValue callee, IReadOnlyList<ALKScriptValue> arguments, ALKScriptToken site);

    Task<ALKScriptValue> Construct(ClassValue classValue, IReadOnlyList<ALKScriptValue> arguments, ALKScriptToken site);
  }
}
