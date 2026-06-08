namespace ALKScript.Interpreter.Common.Evaluation.Values
{
  /// <summary>The single "null" value. Reference-comparable via <see cref="Instance"/>.</summary>
  public sealed class NullValue : ALKScriptValue
  {
    public static readonly NullValue Instance = new NullValue();

    private NullValue()
    {
    }

    public override string TypeName => "null";

    public override bool IsTruthy => false;

    public override string ToString() => "null";
  }
}
