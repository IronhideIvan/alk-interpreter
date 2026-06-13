using ALKScript.Interpreter.Common.Ast;

namespace ALKScript.Interpreter.Common.Evaluation.Values
{
  /// <summary>A function value backed by a host-provided implementation rather than an interpreted body.</summary>
  public sealed class NativeFunctionValue : CallableValue
  {
    public string Name { get; }
    public NativeFunctionImplementation Implementation { get; }

    /// <summary>
    /// For a top-level <c>native function</c> declaration, the declaration
    /// itself — lets structural Capture/Restore (docs/ASYNC_AWAIT_DESIGN.md
    /// Addendum 3) address this value via <c>AstReference</c>, the same way
    /// it addresses <see cref="FunctionValue.Declaration"/>. Null for values
    /// produced any other way (e.g. native methods, see
    /// <see cref="BoundNativeMethod"/>).
    /// </summary>
    public FunctionDecl? Declaration { get; }

    /// <summary>
    /// For a <c>native</c> method bound to an instance (<c>obj.nativeMethod</c>),
    /// the declaring class/method and bound instance — lets structural
    /// Capture/Restore address this value the same way it addresses a bound
    /// script method (<see cref="FunctionValue.DeclaringClass"/>/
    /// <see cref="FunctionValue.BoundInstance"/>). Null for values produced
    /// any other way.
    /// </summary>
    public NativeMethodBinding? BoundNativeMethod { get; }

    private readonly int _arity;

    public NativeFunctionValue(string name, int arity, NativeFunctionImplementation implementation, FunctionDecl? declaration = null, NativeMethodBinding? boundNativeMethod = null)
    {
      Name = name;
      _arity = arity;
      Implementation = implementation;
      Declaration = declaration;
      BoundNativeMethod = boundNativeMethod;
    }

    public override int Arity => _arity;

    public override string TypeName => "function";

    public override string ToString() => $"<native function {Name}>";
  }

  /// <summary>
  /// The provenance of a <see cref="NativeFunctionValue"/> produced for a
  /// <c>native</c> method bound to an instance — see
  /// <see cref="NativeFunctionValue.BoundNativeMethod"/>.
  /// </summary>
  public sealed class NativeMethodBinding
  {
    public MethodDecl Declaration { get; }
    public ClassValue DeclaringClass { get; }
    public InstanceValue BoundInstance { get; }

    public NativeMethodBinding(MethodDecl declaration, ClassValue declaringClass, InstanceValue boundInstance)
    {
      Declaration = declaration;
      DeclaringClass = declaringClass;
      BoundInstance = boundInstance;
    }
  }
}
