using System.Collections.Generic;
using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Common.Evaluation;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Evaluator.Cursor
{
  /// <summary>
  /// Cursor-evaluator counterpart to <see cref="CallInvoker"/> (Step 4 of the
  /// cursor-rewrite plan): resolves a callee to an invocation, binds arguments
  /// to parameters, and runs constructors for <c>new</c> expressions. As with
  /// the rest of the cursor evaluator, every <see cref="StepResult"/> here is
  /// currently <see cref="StepResult.Completed"/> — function/constructor
  /// bodies cannot themselves suspend until <c>await</c> (Step 6) is wired up
  /// — but every sub-evaluation is routed through <see cref="EvaluationCursor"/>
  /// and propagated with the mechanical "if (step.IsAwaiting) return step;"
  /// pattern so later steps compose correctly.
  /// </summary>
  internal sealed class CursorCallInvoker
  {
    private readonly EvaluationCursor _cursor;

    public CursorCallInvoker(EvaluationCursor cursor)
    {
      _cursor = cursor;
    }

    /// <summary>
    /// The resume trail only spans the outermost <see cref="EvaluationCursor.Start"/>
    /// body — a called function/constructor body that itself needs to suspend
    /// is not yet supported in this milestone.
    /// </summary>
    private static StepResult DisallowSuspension(StepResult step, ALKScriptToken site)
    {
      if (step.IsAwaiting)
      {
        throw new RuntimeException(site, "'await' suspending inside a called function or constructor body is not yet supported by the cursor evaluator.");
      }

      return step;
    }

    public StepResult Call(ALKScriptValue callee, IReadOnlyList<ALKScriptValue> arguments, ALKScriptToken site)
    {
      switch (callee)
      {
        case ClassValue classValue:
          return Construct(classValue, arguments, System.Array.Empty<TypeNode>(), site);

        case BaseValue baseValue:
        {
          var step = CallSuperConstructor(baseValue.Superclass, baseValue.Instance, arguments, site);
          if (step.IsAwaiting) return step;
          return StepResult.Completed(NullValue.Instance);
        }

        case CallableValue callable:
          if (arguments.Count != callable.Arity)
          {
            throw new RuntimeException(site, $"Expected {callable.Arity} argument(s) but got {arguments.Count}.");
          }
          return Invoke(callable, arguments, site);

        default:
          throw new RuntimeException(site, $"Cannot call a value of type '{callee.TypeName}'.");
      }
    }

    public StepResult Construct(ClassValue classValue, IReadOnlyList<ALKScriptValue> arguments, IReadOnlyList<TypeNode> typeArguments, ALKScriptToken site)
    {
      var instance = new InstanceValue(classValue, BuildTypeArgumentMap(classValue.Declaration, typeArguments, site));

      // Initialize fields from the whole hierarchy, base class first, so that
      // derived-class initializers can rely on base fields already being set.
      DisallowSuspension(InitializeFields(classValue, instance), site);
      if (_cursor.Signal != null) return StepResult.Completed(NullValue.Instance);

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
        constructorEnvironment.IsInConstructor = true;
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

        DisallowSuspension(_cursor.ExecuteBlock(constructor.Body.Statements, constructorEnvironment), site);

        // A bare "return;" inside a constructor simply ends construction early;
        // a "throw" is left pending so it propagates to the caller.
        if (_cursor.Signal is { Kind: SignalKind.Return })
        {
          _cursor.Signal = null;
        }
      }
      else if (arguments.Count != 0)
      {
        throw new RuntimeException(site, $"Expected 0 argument(s) but got {arguments.Count}.");
      }

      return StepResult.Completed(instance);
    }

    /// <summary>
    /// Initializes all <see cref="FieldDecl"/> members for <paramref name="classValue"/>
    /// and its superclass chain (base-first), evaluating each declared initializer
    /// expression or defaulting to <see cref="NullValue.Instance"/> when no
    /// initializer is present.
    /// </summary>
    private StepResult InitializeFields(ClassValue classValue, InstanceValue instance)
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
              var step = _cursor.Eval(field.Initializer, initEnvironment);
              if (step.IsAwaiting) return step;
              fieldValue = step.Value!;

              if (_cursor.Signal != null) return StepResult.Completed(NullValue.Instance);

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

      return StepResult.Completed(NullValue.Instance);
    }

    private StepResult Invoke(CallableValue callable, IReadOnlyList<ALKScriptValue> arguments, ALKScriptToken site)
    {
      switch (callable)
      {
        case NativeFunctionValue nativeFunction:
          return StepResult.Completed(nativeFunction.Implementation(arguments));

        case CursorNativeFunctionValue cursorNativeFunction:
          return DisallowSuspension(cursorNativeFunction.Implementation(arguments, _cursor), site);

        case FunctionValue function:
          return InvokeFunction(function, arguments, site);

        default:
          throw new RuntimeException(
            AstTokenLocator.EndOfFile,
            $"Unsupported callable '{callable.TypeName}'.");
      }
    }

    private StepResult InvokeFunction(FunctionValue function, IReadOnlyList<ALKScriptValue> arguments, ALKScriptToken site)
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

      return RunBody(function, callEnvironment, site);
    }

    private StepResult RunBody(FunctionValue function, ScriptEnvironment callEnvironment, ALKScriptToken site)
    {
      DisallowSuspension(_cursor.ExecuteBlock(function.Declaration.Body!.Statements, callEnvironment), site);

      // "return" is consumed here — it unwinds no further than the call that
      // produced it. A "throw" is left pending so it propagates to the caller.
      if (_cursor.Signal is { Kind: SignalKind.Return } returned)
      {
        _cursor.Signal = null;
        return StepResult.Completed(returned.Value);
      }

      return StepResult.Completed(NullValue.Instance);
    }

    private StepResult CallSuperConstructor(ClassValue superclass, InstanceValue instance, IReadOnlyList<ALKScriptValue> arguments, ALKScriptToken site)
    {
      var constructor = FindConstructor(superclass);

      if (constructor == null)
      {
        if (arguments.Count != 0)
        {
          throw new RuntimeException(site, $"Expected 0 argument(s) but got {arguments.Count}.");
        }
        return StepResult.Completed(NullValue.Instance);
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

      DisallowSuspension(_cursor.ExecuteBlock(constructor.Body.Statements, env), site);

      if (_cursor.Signal is { Kind: SignalKind.Return })
      {
        _cursor.Signal = null;
      }

      return StepResult.Completed(NullValue.Instance);
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
