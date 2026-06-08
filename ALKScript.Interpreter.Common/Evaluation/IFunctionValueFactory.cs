using ALKScript.Interpreter.Common.Ast;
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
  }
}
