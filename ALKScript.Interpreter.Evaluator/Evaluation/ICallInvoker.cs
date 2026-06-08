using System.Collections.Generic;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Evaluator
{
  /// <summary>
  /// Resolves a callee to an invocation, binds arguments to parameters, and
  /// runs constructors for <c>new</c> expressions.
  /// </summary>
  internal interface ICallInvoker
  {
    ALKScriptValue Call(ALKScriptValue callee, IReadOnlyList<ALKScriptValue> arguments, ALKScriptToken site);

    ALKScriptValue Construct(ClassValue classValue, IReadOnlyList<ALKScriptValue> arguments, ALKScriptToken site);
  }
}
