using System;
using System.Collections.Generic;
using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Common.Evaluation;
using ALKScript.Interpreter.Common.Evaluation.Scheduling;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Evaluator
{
  /// <inheritdoc cref="IFunctionValueFactory"/>
  public class FunctionValueFactory : IFunctionValueFactory
  {
    private readonly ScriptNativeBindings _nativeBindings;
    private readonly ScriptNativeMethodBindings _nativeMethodBindings;
    private readonly IAsyncOperationBinder? _operationBinder;
    private readonly List<PendingOperationValue> _created = new List<PendingOperationValue>();

    /// <summary>
    /// <paramref name="nativeBindings"/> supplies the host implementations for
    /// free-standing, <b>synchronous</b> <c>native function</c> declarations,
    /// keyed by declared name. <paramref name="nativeMethodBindings"/> supplies
    /// them for <c>native</c> methods, keyed by declaring class and member name
    /// — see <see cref="ScriptNativeMethodBindings"/> for why methods need a
    /// separate, class-scoped table.
    ///
    /// <paramref name="operationBinder"/> is the separate host-integration seam
    /// for free-standing <c>native async</c> declarations (see
    /// <see cref="IAsyncOperationBinder"/>): they deliberately do *not* resolve
    /// against <paramref name="nativeBindings"/>, because "when does the host
    /// effect actually start" is no longer simply "when it's called" for them
    /// (lazy/deferred start — see docs/ASYNC_AWAIT_DESIGN.md's core
    /// requirements) — calling one instead immediately produces a
    /// <see cref="PendingOperationValue"/> that records the request and defers
    /// to this binder for if/when it actually runs.
    ///
    /// <c>native async</c> *methods* are intentionally left on the ordinary,
    /// eager <paramref name="nativeMethodBindings"/> path for now: giving a
    /// bound-instance operation a serializable <see cref="PendingOperation"/>
    /// shape raises questions (how does `this` factor into the descriptor a
    /// replay log persists?) the design doc doesn't yet resolve — a
    /// deliberately scoped follow-up, not an oversight.
    /// </summary>
    public FunctionValueFactory(ScriptNativeBindings? nativeBindings = null, ScriptNativeMethodBindings? nativeMethodBindings = null, IAsyncOperationBinder? operationBinder = null)
    {
      _nativeBindings = nativeBindings ?? new ScriptNativeBindings();
      _nativeMethodBindings = nativeMethodBindings ?? new ScriptNativeMethodBindings();
      _operationBinder = operationBinder;
    }

    public string? CurrentModuleSpecifier { get; set; }

    public ALKScriptValue Create(FunctionDecl declaration, ScriptEnvironment closure)
    {
      if (!declaration.IsNative)
      {
        return new FunctionValue(declaration, closure);
      }

      if (declaration.ReturnType.Name == "thunk")
      {
        return CreatePendingOperationFactory(declaration.Name.Lexeme, declaration.Name, declaration.Parameters.Count, ThunkElementType(declaration.ReturnType));
      }

      if (_nativeBindings.TryGetValue(CurrentModuleSpecifier, declaration.Name.Lexeme, out var implementation))
      {
        return new NativeFunctionValue(declaration.Name.Lexeme, declaration.Parameters.Count, implementation, declaration: declaration);
      }

      throw new RuntimeException(declaration.Name, $"Native function '{declaration.Name.Lexeme}' has no host implementation registered.");
    }

    public ALKScriptValue CreateMethod(MethodDecl declaration, ClassValue declaringClass, ScriptEnvironment closure, InstanceValue? boundInstance)
    {
      if (!declaration.IsNative)
      {
        return new FunctionValue(MethodAsFunctionDecl(declaration), closure, boundInstance, declaringClass);
      }

      string className = declaringClass.Declaration.Name.Lexeme;
      string memberName = declaration.Name.Lexeme;

      if (boundInstance == null)
      {
        throw new RuntimeException(declaration.Name, $"Native method '{className}.{memberName}' must be accessed on an instance.");
      }

      if (_nativeMethodBindings.TryGetValue(className, memberName, out var implementation))
      {
        var instance = boundInstance;
        var binding = new NativeMethodBinding(declaration, declaringClass, instance);

        if (declaration.ReturnType.Name == "thunk")
        {
          var elementType = ThunkElementType(declaration.ReturnType);
          return new NativeFunctionValue(memberName, declaration.Parameters.Count, arguments =>
            TagThunkElementType(implementation(instance, arguments), elementType), boundNativeMethod: binding);
        }

        return new NativeFunctionValue(memberName, declaration.Parameters.Count, arguments => implementation(instance, arguments), boundNativeMethod: binding);
      }

      throw new RuntimeException(declaration.Name, $"Native method '{className}.{memberName}' has no host implementation registered.");
    }

    /// <summary>
    /// Produces the value a call to a (free-standing) <c>async native</c>
    /// declaration evaluates to: a <see cref="NativeFunctionValue"/> whose
    /// "implementation" doesn't run any host effect — it just packages the
    /// already-evaluated arguments into a fresh <see cref="PendingOperationValue"/>,
    /// the lazy/deferred-start awaitable that <c>await</c> (or, eventually,
    /// "Discard") will actually start. This is what makes the call itself
    /// "free": the host-side effect is requested, not run, until something
    /// needs its result.
    /// </summary>
    private NativeFunctionValue CreatePendingOperationFactory(string operationName, ALKScriptToken site, int arity, TypeNode? elementType)
    {
      if (_operationBinder == null)
      {
        throw new RuntimeException(site, $"Async native operation '{operationName}' has no IAsyncOperationBinder registered.");
      }

      var binder = _operationBinder;
      var created = _created;
      return new NativeFunctionValue(operationName, arity, arguments =>
      {
        var pending = new PendingOperationValue(new PendingOperation(operationName, arguments), binder, elementType);
        created.Add(pending);
        return pending;
      });
    }

    /// <summary>
    /// The "T" of a declared <c>thunk&lt;T&gt;</c> return type, or <c>null</c>
    /// for a bare <c>thunk</c> (or a non-<c>thunk</c> return type).
    /// </summary>
    private static TypeNode? ThunkElementType(TypeNode returnType) =>
      returnType.Name == "thunk" && returnType.TypeArguments.Count > 0
        ? returnType.TypeArguments[0]
        : null;

    /// <summary>
    /// Tags <paramref name="value"/> with <paramref name="elementType"/> if it
    /// is a <see cref="ThunkValue"/> — the shape a <c>thunk</c>/<c>thunk&lt;T&gt;</c>-declared
    /// native method's host implementation returns directly. Other values
    /// (and <see cref="PendingOperationValue"/>, which is only produced by
    /// <see cref="CreatePendingOperationFactory"/> and tagged there) pass
    /// through unchanged.
    /// </summary>
    private static ALKScriptValue TagThunkElementType(ALKScriptValue value, TypeNode? elementType) =>
      value is ThunkValue thunkValue ? thunkValue.WithElementType(elementType) : value;

    public ALKScriptValue InvokeNativePropertyGetter(string className, string propertyName, InstanceValue instance, ALKScriptToken site)
    {
      string key = "get_" + propertyName;
      if (_nativeMethodBindings.TryGetValue(className, key, out var implementation))
      {
        return implementation(instance, System.Array.Empty<ALKScriptValue>());
      }
      throw new RuntimeException(site, $"Native property getter '{className}.{propertyName}' has no host implementation registered (expected binding key '{className}.{key}').");
    }

    public ALKScriptValue InvokeNativePropertySetter(string className, string propertyName, InstanceValue instance, ALKScriptValue value, ALKScriptToken site)
    {
      string key = "set_" + propertyName;
      if (_nativeMethodBindings.TryGetValue(className, key, out var implementation))
      {
        return implementation(instance, new ALKScriptValue[] { value });
      }
      throw new RuntimeException(site, $"Native property setter '{className}.{propertyName}' has no host implementation registered (expected binding key '{className}.{key}').");
    }

    public void DiscardPending(Action<Exception> onFault)
    {
      if (_operationBinder == null) return;

      foreach (var pending in _created)
      {
        if (!pending.HasStarted)
        {
          _operationBinder.Discard(pending.Operation, onFault);
        }
      }
    }

    public void ReportOperationFaulted(PendingOperation operation, Exception fault)
    {
      _operationBinder?.OnOperationFaulted(operation, fault);
    }

    public void RegisterRestored(PendingOperationValue operation)
    {
      _created.Add(operation);
    }

    /// <summary>
    /// Methods and functions share evaluation logic but not an AST type;
    /// this adapts a <see cref="MethodDecl"/> to the <see cref="FunctionDecl"/>
    /// shape <see cref="FunctionValue"/> expects.
    /// </summary>
    public static FunctionDecl MethodAsFunctionDecl(MethodDecl method)
    {
      return new FunctionDecl(
        method.IsNative,
        method.TypeParameters,
        method.ReturnType,
        method.Name,
        method.Parameters,
        method.Body);
    }
  }
}
