using System.Collections.Generic;

namespace ALKScript.Interpreter.Common.Evaluation.Values
{
  /// <summary>
  /// A mutable, reference-typed array of values, e.g. produced by an
  /// <see cref="Ast.ArrayLiteralExpr"/> or indexed via <see cref="Ast.IndexExpr"/>.
  /// </summary>
  public sealed class ArrayValue : ALKScriptValue
  {
    public List<ALKScriptValue> Items { get; }

    public ArrayValue(List<ALKScriptValue> items)
    {
      Items = items;
    }

    public override string TypeName => "array";

    public override string ToString() => "[" + string.Join(", ", Items) + "]";
  }
}
