namespace ALKScript.Interpreter.Common.Evaluation.Values
{
  /// <summary>
  /// A function value backed by a host-provided implementation that needs to
  /// call back into the evaluator (e.g. <c>array.map(callback)</c> invoking
  /// <c>callback</c> for each element). See <see cref="NativeAsyncFunctionImplementation"/>.
  /// </summary>
  public sealed class NativeAsyncFunctionValue : CallableValue
  {
    public string Name { get; }
    public NativeAsyncFunctionImplementation Implementation { get; }

    private readonly int _arity;

    public NativeAsyncFunctionValue(string name, int arity, NativeAsyncFunctionImplementation implementation)
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
