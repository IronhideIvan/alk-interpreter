using System.Collections.Generic;

namespace ALKScript.Interpreter.Common.Evaluation.Values
{
  /// <summary>
  /// A mutable, reference-typed array of values, e.g. produced by an
  /// <see cref="Ast.ArrayLiteralExpr"/> or indexed via <see cref="Ast.IndexExpr"/>.
  /// Arrays declared with <c>const</c> are frozen via <see cref="Freeze"/>:
  /// any subsequent mutation (push, pop, remove, indexed assignment) is a
  /// <see cref="RuntimeException"/>.
  /// </summary>
  public sealed class ArrayValue : ALKScriptValue
  {
    public List<ALKScriptValue> Items { get; }

    public bool IsFrozen { get; private set; }

    public ArrayValue(List<ALKScriptValue> items)
    {
      Items = items;
    }

    public void Freeze() => IsFrozen = true;

    public override string TypeName => "array";

    public override string ToString() => "[" + string.Join(", ", Items) + "]";
  }
}
