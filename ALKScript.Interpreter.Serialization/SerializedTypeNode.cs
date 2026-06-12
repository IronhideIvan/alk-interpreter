using System.Collections.Generic;
using System.Linq;
using ALKScript.Interpreter.Common.Ast;

namespace ALKScript.Interpreter.Serialization
{
  /// <summary>
  /// JSON-friendly representation of a <see cref="TypeNode"/> — a syntax
  /// fragment with no environment/value references, so it serializes
  /// directly (no reference table needed). Used by the "Phase B" structural
  /// Capture/Restore design (docs/ASYNC_AWAIT_DESIGN.md Addendum 3) for
  /// declared-type annotations (e.g. <c>ScriptEnvironment</c>'s
  /// <c>_types</c>/<c>_currentFunctionReturnType</c>/<c>_currentTypeArguments</c>
  /// and <c>InstanceValue.TypeArguments</c>).
  /// </summary>
  public sealed class SerializedTypeNode
  {
    public string Name { get; set; } = "";

    public List<SerializedTypeNode> TypeArguments { get; set; } = new();

    public int ArrayRank { get; set; }

    public bool IsNullable { get; set; }

    public static SerializedTypeNode From(TypeNode type) => new SerializedTypeNode
    {
      Name = type.Name,
      TypeArguments = type.TypeArguments.Select(From).ToList(),
      ArrayRank = type.ArrayRank,
      IsNullable = type.IsNullable,
    };

    public TypeNode ToTypeNode() => new TypeNode(
      Name,
      TypeArguments.Select(t => t.ToTypeNode()).ToList(),
      ArrayRank,
      IsNullable);
  }
}
