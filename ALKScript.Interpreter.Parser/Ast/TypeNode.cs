using System.Collections.Generic;

namespace ALKScript.Interpreter.Parser.Ast
{
  /// <summary>
  /// Represents a type annotation, e.g. "int", "string[]", "Array&lt;int&gt;?".
  /// Corresponds to the "type" production in the language grammar:
  ///
  ///   type = ( "int" | "long" | "float" | "string" | "bool" | "void" | IDENTIFIER )
  ///          ( "&lt;" type ( "," type )* "&gt;" )?
  ///          ( "[" "]" )*
  ///          "?"? ;
  /// </summary>
  public class TypeNode
  {
    /// <summary>The base type name, e.g. "int" or "Array".</summary>
    public string Name { get; }

    /// <summary>Generic type arguments, e.g. the "T" in "Array&lt;T&gt;". Empty when none are present.</summary>
    public IReadOnlyList<TypeNode> TypeArguments { get; }

    /// <summary>The number of trailing "[]" array-rank suffixes.</summary>
    public int ArrayRank { get; }

    /// <summary>True when the type has a trailing "?" marking it as nullable.</summary>
    public bool IsNullable { get; }

    public TypeNode(string name, IReadOnlyList<TypeNode> typeArguments, int arrayRank, bool isNullable)
    {
      Name = name;
      TypeArguments = typeArguments;
      ArrayRank = arrayRank;
      IsNullable = isNullable;
    }

    public override string ToString()
    {
      string text = Name;

      if (TypeArguments.Count > 0)
      {
        text += "<" + string.Join(", ", TypeArguments) + ">";
      }

      for (int i = 0; i < ArrayRank; i++)
      {
        text += "[]";
      }

      if (IsNullable)
      {
        text += "?";
      }

      return text;
    }
  }
}
