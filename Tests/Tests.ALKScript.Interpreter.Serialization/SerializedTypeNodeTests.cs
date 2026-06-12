using System.Collections.Generic;
using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Serialization;

namespace Tests.ALKScript.Interpreter.Serialization;

/// <summary>
/// Step 1 coverage for the "Phase B" structural Capture/Restore plan (docs:
/// validated-nibbling-narwhal): <see cref="SerializedTypeNode"/>'s round-trip
/// of <see cref="TypeNode"/> syntax fragments (docs/ASYNC_AWAIT_DESIGN.md
/// Addendum 3).
/// </summary>
public class SerializedTypeNodeTests
{
  [Fact]
  public void From_SimpleType_RoundTrips()
  {
    var type = new TypeNode("int", System.Array.Empty<TypeNode>(), arrayRank: 0, isNullable: false);

    var serialized = SerializedTypeNode.From(type);
    var restored = serialized.ToTypeNode();

    Assert.Equal("int", restored.Name);
    Assert.Empty(restored.TypeArguments);
    Assert.Equal(0, restored.ArrayRank);
    Assert.False(restored.IsNullable);
  }

  [Fact]
  public void From_GenericType_RoundTrips()
  {
    var elementType = new TypeNode("int", System.Array.Empty<TypeNode>(), arrayRank: 0, isNullable: false);
    var type = new TypeNode("Array", new List<TypeNode> { elementType }, arrayRank: 0, isNullable: false);

    var serialized = SerializedTypeNode.From(type);
    var restored = serialized.ToTypeNode();

    Assert.Equal("Array", restored.Name);
    Assert.Single(restored.TypeArguments);
    Assert.Equal("int", restored.TypeArguments[0].Name);
  }

  [Fact]
  public void From_NullableArrayType_RoundTrips()
  {
    var type = new TypeNode("string", System.Array.Empty<TypeNode>(), arrayRank: 2, isNullable: true);

    var serialized = SerializedTypeNode.From(type);
    var restored = serialized.ToTypeNode();

    Assert.Equal("string", restored.Name);
    Assert.Equal(2, restored.ArrayRank);
    Assert.True(restored.IsNullable);
  }

  [Fact]
  public void From_NestedGenericType_RoundTrips()
  {
    var innerElement = new TypeNode("string", System.Array.Empty<TypeNode>(), arrayRank: 0, isNullable: false);
    var inner = new TypeNode("Array", new List<TypeNode> { innerElement }, arrayRank: 0, isNullable: false);
    var outer = new TypeNode("Array", new List<TypeNode> { inner }, arrayRank: 0, isNullable: true);

    var serialized = SerializedTypeNode.From(outer);
    var restored = serialized.ToTypeNode();

    Assert.Equal("Array", restored.Name);
    Assert.True(restored.IsNullable);
    Assert.Equal("Array", restored.TypeArguments[0].Name);
    Assert.Equal("string", restored.TypeArguments[0].TypeArguments[0].Name);
  }
}
