using ALKScript.Interpreter.Common.Ast;

namespace ALKScript.Interpreter.Common.Evaluation.Values
{
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
}
