using System.Collections.Generic;
using System.Threading.Tasks;
using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Common.Evaluation;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Evaluator
{
  /// <summary>
  /// Resolves a callee to an invocation, binds arguments to parameters, and
  /// runs constructors for <c>new</c> expressions. Statement execution (running
  /// function/constructor bodies) is delegated through <see cref="IEvaluationContext"/>.
  ///
  /// <c>async</c>/<see cref="Task"/>-returning — see <see cref="IEvaluationContext"/>
  /// for why: a function/constructor body may itself contain an <c>await</c>
  /// that suspends mid-call, and this is the plumbing that lets the call resume
  /// later exactly where it left off.
  /// </summary>
  internal class CallInvoker : ICallInvoker
  {
    private readonly IEvaluationContext _context;

    public CallInvoker(IEvaluationContext context)
    {
      _context = context;
    }

    public async Task<ALKScriptValue> Call(ALKScriptValue callee, IReadOnlyList<ALKScriptValue> arguments, ALKScriptToken site)
    {
      switch (callee)
      {
        case ClassValue classValue:
          return await Construct(classValue, arguments, site);

        case BaseValue baseValue:
          await CallSuperConstructor(baseValue.Superclass, baseValue.Instance, arguments, site);
          return NullValue.Instance;

        case CallableValue callable:
          if (arguments.Count != callable.Arity)
          {
            throw new RuntimeException(site, $"Expected {callable.Arity} argument(s) but got {arguments.Count}.");
          }
          return await Invoke(callable, arguments);

        default:
          throw new RuntimeException(site, $"Cannot call a value of type '{callee.TypeName}'.");
      }
    }

    public async Task<ALKScriptValue> Construct(ClassValue classValue, IReadOnlyList<ALKScriptValue> arguments, ALKScriptToken site)
    {
      var instance = new InstanceValue(classValue);
      var constructor = FindConstructor(classValue);

      if (constructor != null)
      {
        if (arguments.Count != constructor.Parameters.Count)
        {
          throw new RuntimeException(site, $"Expected {constructor.Parameters.Count} argument(s) but got {arguments.Count}.");
        }

        var constructorEnvironment = new ScriptEnvironment(ClassEnvironments.For(classValue));
        constructorEnvironment.Define("this", instance);
        if (classValue.Superclass != null)
        {
          constructorEnvironment.Define("base", new BaseValue(classValue.Superclass, instance));
        }

        for (int i = 0; i < constructor.Parameters.Count; i++)
        {
          constructorEnvironment.Define(constructor.Parameters[i].Name, arguments[i]);
        }

        await _context.ExecuteBlock(constructor.Body.Statements, constructorEnvironment);

        // A bare "return;" inside a constructor simply ends construction early;
        // a "throw" is left pending so it propagates to the caller.
        if (_context.Signal is { Kind: SignalKind.Return })
        {
          _context.Signal = null;
        }
      }
      else if (arguments.Count != 0)
      {
        throw new RuntimeException(site, $"Expected 0 argument(s) but got {arguments.Count}.");
      }

      return instance;
    }

    private async Task<ALKScriptValue> Invoke(CallableValue callable, IReadOnlyList<ALKScriptValue> arguments)
    {
      switch (callable)
      {
        case NativeFunctionValue nativeFunction:
          return nativeFunction.Implementation(arguments);

        case FunctionValue function:
          return await InvokeFunction(function, arguments);

        default:
          throw new RuntimeException(
            AstTokenLocator.EndOfFile,
            $"Unsupported callable '{callable.TypeName}'.");
      }
    }

    private Task<ALKScriptValue> InvokeFunction(FunctionValue function, IReadOnlyList<ALKScriptValue> arguments)
    {
      var callEnvironment = new ScriptEnvironment(function.Closure);

      if (function.BoundInstance != null)
      {
        callEnvironment.Define("this", function.BoundInstance);
        if (function.DeclaringClass?.Superclass != null)
        {
          callEnvironment.Define("base", new BaseValue(function.DeclaringClass.Superclass, function.BoundInstance));
        }
      }

      for (int i = 0; i < function.Declaration.Parameters.Count; i++)
      {
        callEnvironment.Define(function.Declaration.Parameters[i].Name, arguments[i]);
      }

      // A non-"async" function/method runs its body to completion before
      // returning, exactly as before — calling it suspends the caller (via
      // the "await" on RunBody below) for as long as the body takes, with no
      // observable "in-flight operation" of its own.
      //
      // An "async" function/method instead returns a TaskValue immediately,
      // wrapping whatever RunBody produces — calling it starts the body
      // running (synchronously, up to its first genuine suspension point,
      // exactly like a C# "async Task<T>" method), and hands back a
      // (possibly still-pending) handle the caller can "await" later, do
      // other work alongside, or even ignore (fire-and-forget). This is what
      // makes "let t = asyncFn(); ...; await t;" meaningfully different from
      // "await asyncFn();" — the defining shape of async/await.
      var bodyTask = RunBody(function, callEnvironment);

      if (function.Declaration.IsAsync)
      {
        return Task.FromResult<ALKScriptValue>(new TaskValue(bodyTask));
      }

      return bodyTask;
    }

    private async Task<ALKScriptValue> RunBody(FunctionValue function, ScriptEnvironment callEnvironment)
    {
      await _context.ExecuteBlock(function.Declaration.Body!.Statements, callEnvironment);

      // "return" is consumed here — it unwinds no further than the call that
      // produced it. A "throw" is left pending so it propagates to the caller.
      if (_context.Signal is { Kind: SignalKind.Return } returned)
      {
        _context.Signal = null;
        return returned.Value;
      }

      return NullValue.Instance;
    }

    private async Task CallSuperConstructor(ClassValue superclass, InstanceValue instance, IReadOnlyList<ALKScriptValue> arguments, ALKScriptToken site)
    {
      var constructor = FindConstructor(superclass);

      if (constructor == null)
      {
        if (arguments.Count != 0)
        {
          throw new RuntimeException(site, $"Expected 0 argument(s) but got {arguments.Count}.");
        }
        return;
      }

      if (arguments.Count != constructor.Parameters.Count)
      {
        throw new RuntimeException(site, $"Expected {constructor.Parameters.Count} argument(s) but got {arguments.Count}.");
      }

      var env = new ScriptEnvironment(ClassEnvironments.For(superclass));
      env.Define("this", instance);
      if (superclass.Superclass != null)
      {
        env.Define("base", new BaseValue(superclass.Superclass, instance));
      }

      for (int i = 0; i < constructor.Parameters.Count; i++)
      {
        env.Define(constructor.Parameters[i].Name, arguments[i]);
      }

      await _context.ExecuteBlock(constructor.Body.Statements, env);

      if (_context.Signal is { Kind: SignalKind.Return })
      {
        _context.Signal = null;
      }
    }

    private static ConstructorDecl? FindConstructor(ClassValue classValue)
    {
      foreach (var member in classValue.Declaration.Members)
      {
        if (member is ConstructorDecl constructor)
        {
          return constructor;
        }
      }

      return null;
    }
  }
}
