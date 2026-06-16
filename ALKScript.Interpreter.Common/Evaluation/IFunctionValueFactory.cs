using System;
using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Common.Evaluation.Scheduling;
using ALKScript.Interpreter.Common.Evaluation.Values;

namespace ALKScript.Interpreter.Common.Evaluation
{
  /// <summary>
  /// Builds the callable <see cref="ALKScriptValue"/> for a function or method
  /// declaration, resolving <c>native</c> declarations against host bindings.
  /// </summary>
  public interface IFunctionValueFactory
  {
    /// <summary>
    /// The identifier of the module whose declarations are currently being
    /// evaluated, or <c>null</c> when executing global preludes or inline
    /// (non-module) scripts. The evaluator sets this before starting each
    /// module segment so that <see cref="Create"/> can look up
    /// module-qualified native bindings.
    /// </summary>
    string? CurrentModuleSpecifier { get; set; }

    /// <summary>
    /// Creates the callable value for <paramref name="declaration"/> closing
    /// over <paramref name="closure"/>. A <c>native</c> declaration with no
    /// matching host binding fails with a <see cref="RuntimeException"/>.
    /// </summary>
    ALKScriptValue Create(FunctionDecl declaration, ScriptEnvironment closure);

    /// <summary>
    /// Creates the callable value for the method <paramref name="declaration"/>
    /// — declared on <paramref name="declaringClass"/> — closing over
    /// <paramref name="closure"/> and bound to <paramref name="boundInstance"/>
    /// (the receiver for instance access; <c>null</c> for access through a
    /// <see cref="ClassValue"/> directly).
    ///
    /// An ordinary method becomes a <see cref="FunctionValue"/> bound the same
    /// way <see cref="Create"/> would produce one. A <c>native</c> method
    /// instead resolves against the host's <see cref="ScriptNativeMethodBindings"/>
    /// — keyed by <paramref name="declaringClass"/>'s name and the method's
    /// declared name — and the resulting <see cref="NativeFunctionValue"/>'s
    /// implementation is invoked with <paramref name="boundInstance"/> as its
    /// receiver, so host code can read and mutate <see cref="InstanceValue.Fields"/>
    /// (e.g. to back a collection class with real, host-managed storage). A
    /// <c>native</c> method requires a non-null <paramref name="boundInstance"/>
    /// — it has no meaning accessed directly through a class — and, like
    /// <see cref="Create"/>, fails with a <see cref="RuntimeException"/> when
    /// no matching host binding is registered.
    /// </summary>
    ALKScriptValue CreateMethod(MethodDecl declaration, ClassValue declaringClass, ScriptEnvironment closure, InstanceValue? boundInstance);

    /// <summary>
    /// Fires off any <c>async native</c> operations that were called but never
    /// <c>await</c>ed — the end-of-script "Discard" path (see
    /// <see cref="IAsyncOperationBinder.Discard"/>). No-op if no
    /// <see cref="IAsyncOperationBinder"/> was registered or if every created
    /// <see cref="PendingOperationValue"/> was already started by <c>await</c>.
    /// </summary>
    void DiscardPending(Action<Exception> onFault);

    /// <summary>
    /// Routes an individual operation fault to the host (see
    /// <see cref="IAsyncOperationBinder.OnOperationFaulted"/>), called per
    /// faulted member of a <c>await [a, b, …]</c>. No-op if no binder.
    /// </summary>
    void ReportOperationFaulted(PendingOperation operation, Exception fault);

    /// <summary>
    /// Registers <paramref name="operation"/> — already started via
    /// <see cref="IAsyncOperationBinder.Start"/> while reissuing a captured
    /// suspension on "Phase B" structural Restore (docs/ASYNC_AWAIT_DESIGN.md
    /// Addendum 3, Step 14) — so end-of-script <see cref="DiscardPending"/>
    /// accounts for it the same as any other operation created during this
    /// run.
    /// </summary>
    void RegisterRestored(PendingOperationValue operation);
  }
}
