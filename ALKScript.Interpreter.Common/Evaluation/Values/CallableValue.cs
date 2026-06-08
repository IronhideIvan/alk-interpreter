using System.Collections.Generic;
using ALKScript.Interpreter.Common.Ast;

namespace ALKScript.Interpreter.Common.Evaluation.Values
{
  /// <summary>Base type for any value that can appear as the callee of a <see cref="CallExpr"/>.</summary>
  public abstract class CallableValue : ALKScriptValue
  {
    /// <summary>The number of arguments this callable expects.</summary>
    public abstract int Arity { get; }
  }

  /// <summary>
  /// A user-defined function or method value: a declaration paired with the
  /// environment in which it was declared (its closure), so that nested
  /// functions can capture enclosing locals.
  /// </summary>
  public sealed class FunctionValue : CallableValue
  {
    public FunctionDecl Declaration { get; }
    public Environment Closure { get; }

    /// <summary>
    /// For methods, the instance "this" is bound to when the method is looked
    /// up on an <see cref="InstanceValue"/>; null for free-standing functions.
    /// </summary>
    public InstanceValue? BoundInstance { get; }

    public FunctionValue(FunctionDecl declaration, Environment closure, InstanceValue? boundInstance = null)
    {
      Declaration = declaration;
      Closure = closure;
      BoundInstance = boundInstance;
    }

    public override int Arity => Declaration.Parameters.Count;

    public override string TypeName => "function";

    public override string ToString() => $"<function {Declaration.Name.Lexeme}>";

    /// <summary>Returns a copy of this function bound to <paramref name="instance"/>, for method dispatch.</summary>
    public FunctionValue BindTo(InstanceValue instance) => new FunctionValue(Declaration, Closure, instance);
  }

  /// <summary>
  /// Implements the body of a <c>native</c> declaration: invoked directly by
  /// the host runtime with already-evaluated argument values.
  /// </summary>
  public delegate ALKScriptValue NativeFunctionImplementation(IReadOnlyList<ALKScriptValue> arguments);

  /// <summary>A function value backed by a host-provided implementation rather than an interpreted body.</summary>
  public sealed class NativeFunctionValue : CallableValue
  {
    public string Name { get; }
    public NativeFunctionImplementation Implementation { get; }

    private readonly int _arity;

    public NativeFunctionValue(string name, int arity, NativeFunctionImplementation implementation)
    {
      Name = name;
      _arity = arity;
      Implementation = implementation;
    }

    public override int Arity => _arity;

    public override string TypeName => "function";

    public override string ToString() => $"<native function {Name}>";
  }
}
