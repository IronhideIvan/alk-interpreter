using System.Collections.Generic;

namespace ALKScript.Interpreter.Common.Evaluation.Values
{
  /// <summary>
  /// The host implementations for scripts' <c>native</c> method declarations,
  /// keyed by declaring class name and member name.
  ///
  /// Unlike free-standing <c>native function</c>s — which live in one flat,
  /// script-wide namespace and so resolve by name alone via
  /// <see cref="ScriptNativeBindings"/> — methods are scoped to their
  /// declaring class, and different classes are free to declare same-named
  /// natives (e.g. both an <c>Array</c> and a <c>Set</c> might declare a
  /// native <c>add</c>). The two-part key keeps those bindings distinct
  /// without requiring host code to invent qualified names.
  ///
  /// "Declaring class" — not the runtime type of the receiving instance —
  /// because that's where <see cref="ALKScript.Interpreter.Common.Ast.MethodDecl.IsNative"/>
  /// is asserted; a subclass that merely inherits a native method resolves it
  /// against the same binding its declaring superclass registered.
  /// </summary>
  public sealed class ScriptNativeMethodBindings
  {
    private readonly Dictionary<(string ClassName, string MemberName), NativeMethodImplementation> _bindings;

    public ScriptNativeMethodBindings()
    {
      _bindings = new Dictionary<(string, string), NativeMethodImplementation>();
    }

    /// <summary>Registers (or replaces) the host implementation for <paramref name="memberName"/> as declared on <paramref name="className"/>.</summary>
    public NativeMethodImplementation this[string className, string memberName]
    {
      get => _bindings[(className, memberName)];
      set => _bindings[(className, memberName)] = value;
    }

    public bool TryGetValue(string className, string memberName, out NativeMethodImplementation implementation)
      => _bindings.TryGetValue((className, memberName), out implementation!);
  }
}
