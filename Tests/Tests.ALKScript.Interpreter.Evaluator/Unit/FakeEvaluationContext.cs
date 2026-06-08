using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Common.Evaluation;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Common.Token;
using ALKScript.Interpreter.Evaluator;

namespace Tests.ALKScript.Interpreter.Evaluator.Unit;

/// <summary>
/// A test double for <see cref="IEvaluationContext"/>: lets a unit test drive
/// <see cref="StatementExecutor"/>, <see cref="ExpressionEvaluator"/> or
/// <see cref="CallInvoker"/> in isolation by stubbing out the operations it
/// recurses through. Each operation defaults to throwing, so a test that
/// triggers an unstubbed recursive call fails loudly rather than silently
/// exercising production collaborators.
///
/// The stub delegates (<c>*Impl</c>) stay synchronous — none of the unit tests
/// need to model actual suspension — but the interface members wrap their
/// results in completed <see cref="Task"/>s so the fake satisfies the
/// <see cref="Task"/>-returning <see cref="IEvaluationContext"/> surface.
/// </summary>
internal sealed class FakeEvaluationContext : IEvaluationContext
{
  public Signal? Signal { get; set; }

  public Action<Stmt, ScriptEnvironment> ExecuteImpl { get; set; } =
    (_, _) => throw new InvalidOperationException("Execute was not expected.");

  public Action<IReadOnlyList<Stmt>, ScriptEnvironment> ExecuteBlockImpl { get; set; } =
    (_, _) => throw new InvalidOperationException("ExecuteBlock was not expected.");

  public Func<Expr, ScriptEnvironment, ALKScriptValue> EvalImpl { get; set; } =
    (_, _) => throw new InvalidOperationException("Eval was not expected.");

  public Func<ALKScriptValue, IReadOnlyList<ALKScriptValue>, ALKScriptToken, ALKScriptValue> CallImpl { get; set; } =
    (_, _, _) => throw new InvalidOperationException("Call was not expected.");

  public Func<ClassValue, IReadOnlyList<ALKScriptValue>, ALKScriptToken, ALKScriptValue> ConstructImpl { get; set; } =
    (_, _, _) => throw new InvalidOperationException("Construct was not expected.");

  public Task Execute(Stmt statement, ScriptEnvironment environment)
  {
    ExecuteImpl(statement, environment);
    return Task.CompletedTask;
  }

  public Task ExecuteBlock(IReadOnlyList<Stmt> statements, ScriptEnvironment environment)
  {
    ExecuteBlockImpl(statements, environment);
    return Task.CompletedTask;
  }

  public Task<ALKScriptValue> Eval(Expr expression, ScriptEnvironment environment)
    => Task.FromResult(EvalImpl(expression, environment));

  public Task<ALKScriptValue> Call(ALKScriptValue callee, IReadOnlyList<ALKScriptValue> arguments, ALKScriptToken site)
    => Task.FromResult(CallImpl(callee, arguments, site));

  public Task<ALKScriptValue> Construct(ClassValue classValue, IReadOnlyList<ALKScriptValue> arguments, ALKScriptToken site)
    => Task.FromResult(ConstructImpl(classValue, arguments, site));
}
