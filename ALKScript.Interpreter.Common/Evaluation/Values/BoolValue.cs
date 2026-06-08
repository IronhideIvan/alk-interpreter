namespace ALKScript.Interpreter.Common.Evaluation.Values
{
  /// <summary>A boolean value. Interned via <see cref="True"/>/<see cref="False"/>.</summary>
  public sealed class BoolValue : ALKScriptValue
  {
    public static readonly BoolValue True = new BoolValue(true);
    public static readonly BoolValue False = new BoolValue(false);

    public bool Value { get; }

    private BoolValue(bool value)
    {
      Value = value;
    }

    public static BoolValue Of(bool value) => value ? True : False;

    public override string TypeName => "bool";

    public override bool IsTruthy => Value;

    public override string ToString() => Value ? "true" : "false";
  }
}
