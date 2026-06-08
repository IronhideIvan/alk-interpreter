using System.Collections.Generic;

namespace ALKScript.Interpreter.Common.Evaluation.Values
{
  /// <summary>An instantiated object: a class paired with its own field storage.</summary>
  public sealed class InstanceValue : ALKScriptValue
  {
    public ClassValue Class { get; }
    public Dictionary<string, ALKScriptValue> Fields { get; }

    public InstanceValue(ClassValue @class)
    {
      Class = @class;
      Fields = new Dictionary<string, ALKScriptValue>();
    }

    public override string TypeName => Class.Declaration.Name.Lexeme;

    public override string ToString() => $"<instance of {Class.Declaration.Name.Lexeme}>";
  }
}
