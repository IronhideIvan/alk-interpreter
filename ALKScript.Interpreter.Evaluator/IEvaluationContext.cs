using System.Collections.Generic;
using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Common.Evaluation;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Evaluator
{
  /// <summary>
  /// The shared surface that <see cref="StatementExecutor"/>,
  /// <see cref="ExpressionEvaluator"/> and <see cref="CallInvoker"/> recurse
  /// through, plus the pending-signal slot they coordinate on to implement
  /// "return"/"throw" unwinding without .NET exceptions.
  ///
  /// The three components call into each other (statements evaluate
  /// expressions, expressions perform calls, calls execute statement blocks),
  /// which would otherwise force them into a constructor cycle. Routing those
  /// calls through this interface — implemented by <see cref="ProgramEvaluator"/>,
  /// which composes and wires up the three — lets each be built independently
  /// and breaks the cycle.
  /// </summary>
  internal interface IEvaluationContext
  {
    /// <summary>
    /// A pending non-local exit ("return" or "throw") raised while executing
    /// the statement or evaluating the expression currently in progress.
    /// Checked after each sub-execution/sub-evaluation so control can unwind
    /// to the right handler (a function call, a "try", or the top level).
    /// </summary>
    Signal? Signal { get; set; }

    void Execute(Stmt statement, ScriptEnvironment environment);

    void ExecuteBlock(IReadOnlyList<Stmt> statements, ScriptEnvironment environment);

    ALKScriptValue Eval(Expr expression, ScriptEnvironment environment);

    ALKScriptValue Call(ALKScriptValue callee, IReadOnlyList<ALKScriptValue> arguments, ALKScriptToken site);

    ALKScriptValue Construct(ClassValue classValue, IReadOnlyList<ALKScriptValue> arguments, ALKScriptToken site);
  }
}
