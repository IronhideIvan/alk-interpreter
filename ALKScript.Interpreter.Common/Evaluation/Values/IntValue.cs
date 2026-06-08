namespace ALKScript.Interpreter.Common.Evaluation.Values
{
  /// <summary>An integer value, backed by a 64-bit signed integer (ALKScript's "int"/"long").</summary>
  public sealed class IntValue : ALKScriptValue
  {
    public long Value { get; }

    public IntValue(long value)
    {
      Value = value;
    }

    public override string TypeName => "int";

    public override string ToString() => Value.ToString();
  }
}
