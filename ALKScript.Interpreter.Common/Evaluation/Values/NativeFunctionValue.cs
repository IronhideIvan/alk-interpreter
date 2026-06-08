namespace ALKScript.Interpreter.Common.Evaluation.Values
{
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
