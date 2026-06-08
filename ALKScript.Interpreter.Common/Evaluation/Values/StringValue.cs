namespace ALKScript.Interpreter.Common.Evaluation.Values
{
  /// <summary>A string value.</summary>
  public sealed class StringValue : ALKScriptValue
  {
    public string Value { get; }

    public StringValue(string value)
    {
      Value = value;
    }

    public override string TypeName => "string";

    public override string ToString() => Value;
  }
}
