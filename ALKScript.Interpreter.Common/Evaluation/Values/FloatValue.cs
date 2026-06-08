namespace ALKScript.Interpreter.Common.Evaluation.Values
{
  /// <summary>A floating-point value, backed by a double (ALKScript's "float").</summary>
  public sealed class FloatValue : ALKScriptValue
  {
    public double Value { get; }

    public FloatValue(double value)
    {
      Value = value;
    }

    public override string TypeName => "float";

    public override string ToString() => Value.ToString();
  }
}
