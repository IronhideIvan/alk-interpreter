namespace ALKScript.Interpreter.Common.Evaluation.Values
{
  /// <summary>
  /// The runtime representation of the <c>base</c> keyword inside a constructor
  /// or method body: a pointer to the superclass paired with the current instance,
  /// so <c>base(args)</c> invokes the superclass constructor on the existing
  /// object and <c>base.method(args)</c> dispatches through the superclass
  /// method table — both without creating a new instance.
  /// </summary>
  public sealed class BaseValue : ALKScriptValue
  {
    public ClassValue Superclass { get; }
    public InstanceValue Instance { get; }

    public BaseValue(ClassValue superclass, InstanceValue instance)
    {
      Superclass = superclass;
      Instance = instance;
    }

    public override string TypeName => "base";

    public override string ToString() => "<base>";
  }
}
