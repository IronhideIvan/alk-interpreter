using System.Collections.Generic;
using ALKScript.Interpreter.Common.Ast;

namespace ALKScript.Interpreter.Common.Evaluation.Values
{
  /// <summary>An instantiated object: a class paired with its own field storage.</summary>
  public sealed class InstanceValue : ALKScriptValue
  {
    public ClassValue Class { get; }
    public Dictionary<string, ALKScriptValue> Fields { get; }

    /// <summary>
    /// The substitution map (e.g. <c>{"T" -> int}</c>) recorded when this
    /// instance was constructed via <c>new Box&lt;int&gt;(...)</c>. Empty when
    /// type arguments were omitted (<c>new Box(...)</c>), in which case
    /// generic members remain fully erased/unconstrained.
    /// </summary>
    public IReadOnlyDictionary<string, TypeNode> TypeArguments { get; }

    public InstanceValue(ClassValue @class, IReadOnlyDictionary<string, TypeNode>? typeArguments = null)
    {
      Class = @class;
      Fields = new Dictionary<string, ALKScriptValue>();
      TypeArguments = typeArguments ?? new Dictionary<string, TypeNode>();
    }

    public override string TypeName => Class.Declaration.Name.Lexeme;

    public override string ToString() => $"<instance of {Class.Declaration.Name.Lexeme}>";
  }
}
