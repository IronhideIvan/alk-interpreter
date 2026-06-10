namespace ALKScript.Interpreter.Common.Evaluation.Values
{
  /// <summary>
  /// A single member of an enum, e.g. <c>Color.Red</c>. Each member is a
  /// singleton instance owned by the declaring <see cref="EnumTypeValue"/>,
  /// so reference equality (used by <c>Operators.AreEqual</c>) is sufficient
  /// to compare enum values.
  /// </summary>
  public sealed class EnumValue : ALKScriptValue
  {
    public string EnumName { get; }
    public string MemberName { get; }
    public long Value { get; }

    public EnumValue(string enumName, string memberName, long value)
    {
      EnumName = enumName;
      MemberName = memberName;
      Value = value;
    }

    public override string TypeName => EnumName;

    public override string ToString() => $"{EnumName}.{MemberName}";
  }
}
