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
          return await Construct(classValue, arguments, System.Array.Empty<TypeNode>(), site);

        case BaseValue baseValue:
          await CallSuperConstructor(baseValue.Superclass, baseValue.Instance, arguments, site);
          return NullValue.Instance;

        case CallableValue callable:
          if (arguments.Count != callable.Arity)
          {
            throw new RuntimeException(site, $"Expected {callable.Arity} argument(s) but got {arguments.Count}.");
          }
          return await Invoke(callable, arguments, site);

        default:
          throw new RuntimeException(site, $"Cannot call a value of type '{callee.TypeName}'.");
      }
    }

    public async Task<ALKScriptValue> Construct(ClassValue classValue, IReadOnlyList<ALKScriptValue> arguments, IReadOnlyList<TypeNode> typeArguments, ALKScriptToken site)
    {
      var instance = new InstanceValue(classValue, BuildTypeArgumentMap(classValue.Declaration, typeArguments, site));

      // Initialize fields from the whole hierarchy, base class first, so that
      // derived-class initializers can rely on base fields already being set.
      await InitializeFields(classValue, instance);
      if (_context.Signal != null) return NullValue.Instance;

      var constructor = FindConstructor(classValue);

      if (constructor != null)
      {
        if (arguments.Count != constructor.Parameters.Count)
        {
          throw new RuntimeException(site, $"Expected {constructor.Parameters.Count} argument(s) but got {arguments.Count}.");
        }

        var constructorEnvironment = new ScriptEnvironment(ClassEnvironments.For(classValue));
        constructorEnvironment.CurrentClass = classValue;
        constructorEnvironment.CurrentTypeArguments = instance.TypeArguments;
        constructorEnvironment.Define("this", instance);
        if (classValue.Superclass != null)
        {
          constructorEnvironment.Define("base", new BaseValue(classValue.Superclass, instance));
        }

        for (int i = 0; i < constructor.Parameters.Count; i++)
        {
          var parameter = constructor.Parameters[i];
          TypeChecking.EnsureAssignable(parameter.Type, arguments[i], site, $"parameter '{parameter.Name}'", constructorEnvironment);
          constructorEnvironment.Define(parameter.Name, arguments[i], parameter.Type);
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

    /// <summary>
    /// Initializes all <see cref="FieldDecl"/> members for <paramref name="classValue"/>
    /// and its superclass chain (base-first), evaluating each declared initializer
    /// expression or defaulting to <see cref="NullValue.Instance"/> when no
    /// initializer is present.
    /// </summary>
    private async Task InitializeFields(ClassValue classValue, InstanceValue instance)
    {
      // Collect the hierarchy base-to-derived so base fields are initialized first.
      var hierarchy = new List<ClassValue>();
      for (ClassValue? c = classValue; c != null; c = c.Superclass)
      {
        hierarchy.Insert(0, c);
      }

      var initEnvironment = new ScriptEnvironment(ClassEnvironments.For(classValue));
      initEnvironment.Define("this", instance);
      initEnvironment.CurrentTypeArguments = instance.TypeArguments;

      foreach (var cls in hierarchy)
      {
        foreach (var member in cls.Declaration.Members)
        {
          if (member is FieldDecl field && !field.IsStatic)
          {
            ALKScriptValue fieldValue;
            if (field.Initializer != null)
            {
              fieldValue = await _context.Eval(field.Initializer, initEnvironment);
              if (_context.Signal != null) return;

              TypeChecking.EnsureAssignable(field.Type, fieldValue, field.Name, $"field '{field.Name.Lexeme}'", initEnvironment);
            }
            else
            {
              fieldValue = NullValue.Instance;
            }

            instance.Fields[field.Name.Lexeme] = fieldValue;
          }
        }
      }
    }

    private async Task<ALKScriptValue> Invoke(CallableValue callable, IReadOnlyList<ALKScriptValue> arguments, ALKScriptToken site)
    {
      switch (callable)
      {
        case NativeFunctionValue nativeFunction:
          return nativeFunction.Implementation(arguments);

        case FunctionValue function:
          return await InvokeFunction(function, arguments, site);

        default:
          throw new RuntimeException(
            AstTokenLocator.EndOfFile,
            $"Unsupported callable '{callable.TypeName}'.");
      }
    }

    private Task<ALKScriptValue> InvokeFunction(FunctionValue function, IReadOnlyList<ALKScriptValue> arguments, ALKScriptToken site)
    {
      var callEnvironment = new ScriptEnvironment(function.Closure);
      callEnvironment.CurrentClass = function.DeclaringClass;
      callEnvironment.CurrentFunctionReturnType = function.Declaration.ReturnType;
      callEnvironment.CurrentTypeArguments = function.BoundInstance?.TypeArguments;

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
        var parameter = function.Declaration.Parameters[i];
        TypeChecking.EnsureAssignable(parameter.Type, arguments[i], site, $"parameter '{parameter.Name}'", callEnvironment);
        callEnvironment.Define(parameter.Name, arguments[i], parameter.Type);
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
      env.CurrentClass = superclass;
      env.Define("this", instance);
      env.CurrentTypeArguments = instance.TypeArguments;
      if (superclass.Superclass != null)
      {
        env.Define("base", new BaseValue(superclass.Superclass, instance));
      }

      for (int i = 0; i < constructor.Parameters.Count; i++)
      {
        var parameter = constructor.Parameters[i];
        TypeChecking.EnsureAssignable(parameter.Type, arguments[i], site, $"parameter '{parameter.Name}'", env);
        env.Define(parameter.Name, arguments[i], parameter.Type);
      }

      await _context.ExecuteBlock(constructor.Body.Statements, env);

      if (_context.Signal is { Kind: SignalKind.Return })
      {
        _context.Signal = null;
      }
    }

    /// <summary>
    /// Builds the substitution map (e.g. <c>{"T" -> int}</c>) from the type
    /// arguments supplied at a <c>new</c> site. A generic class declaration
    /// (one with one or more <see cref="ClassDecl.TypeParameters"/>) requires
    /// type arguments to be supplied with the matching count — omitting them,
    /// or supplying the wrong count, is a <see cref="RuntimeException"/>. A
    /// non-generic class declaration (no type parameters) yields an empty map.
    /// </summary>
    private static IReadOnlyDictionary<string, TypeNode> BuildTypeArgumentMap(ClassDecl declaration, IReadOnlyList<TypeNode> typeArguments, ALKScriptToken site)
    {
      if (declaration.TypeParameters.Count == 0)
      {
        return new Dictionary<string, TypeNode>();
      }

      if (typeArguments.Count != declaration.TypeParameters.Count)
      {
        throw new RuntimeException(site, $"'{declaration.Name.Lexeme}' expects {declaration.TypeParameters.Count} type argument(s) but got {typeArguments.Count}.");
      }

      var map = new Dictionary<string, TypeNode>();
      for (int i = 0; i < declaration.TypeParameters.Count; i++)
      {
        map[declaration.TypeParameters[i]] = typeArguments[i];
      }

      return map;
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
