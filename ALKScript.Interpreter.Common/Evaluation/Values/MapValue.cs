using System.Collections.Generic;
using ALKScript.Interpreter.Common.Ast;

namespace ALKScript.Interpreter.Common.Evaluation.Values
{
  /// <summary>
  /// A mutable map (dictionary) value: <c>map&lt;K, V&gt;</c>.
  /// Keys are stored as their string representation when K is string,
  /// or their underlying long value when K is int or an enum.
  /// </summary>
  public sealed class MapValue : ALKScriptValue
  {
    public Dictionary<ALKScriptValue, ALKScriptValue> Entries { get; }
    public TypeNode KeyType { get; }
    public TypeNode ValueType { get; }

    public MapValue(TypeNode keyType, TypeNode valueType)
    {
      KeyType = keyType;
      ValueType = valueType;
      Entries = new Dictionary<ALKScriptValue, ALKScriptValue>(MapKeyComparer.Instance);
    }

    public override string TypeName => "map";

    public override string ToString() => "<map>";
  }

  /// <summary>
  /// Equality comparer for map keys that compares by value (not reference).
  /// String, int, and enum keys all compare by their content.
  /// </summary>
  internal sealed class MapKeyComparer : IEqualityComparer<ALKScriptValue>
  {
    public static readonly MapKeyComparer Instance = new MapKeyComparer();

    public bool Equals(ALKScriptValue? x, ALKScriptValue? y)
    {
      if (x is null && y is null) return true;
      if (x is null || y is null) return false;
      if (x is StringValue sx && y is StringValue sy) return sx.Value == sy.Value;
      if (x is IntValue ix && y is IntValue iy) return ix.Value == iy.Value;
      if (x is EnumValue ex && y is EnumValue ey) return ex.EnumName == ey.EnumName && ex.MemberName == ey.MemberName;
      return false;
    }

    public int GetHashCode(ALKScriptValue obj)
    {
      switch (obj)
      {
        case StringValue s: return s.Value.GetHashCode();
        case IntValue i: return i.Value.GetHashCode();
        case EnumValue e: return (e.EnumName + "." + e.MemberName).GetHashCode();
        default: return obj.GetHashCode();
      }
    }
  }
}
