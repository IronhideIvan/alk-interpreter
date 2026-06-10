using System.Collections.Generic;

namespace ALKScript.Interpreter.Common.Evaluation.Values
{
  /// <summary>
  /// A namespace object produced by a namespace import ("import * as Foo from ..."),
  /// exposing the imported module's top-level bindings as members.
  /// </summary>
  public sealed class NamespaceValue : ALKScriptValue
  {
    public string Name { get; }
    public IReadOnlyDictionary<string, ALKScriptValue> Members { get; }

    public NamespaceValue(string name, IReadOnlyDictionary<string, ALKScriptValue> members)
    {
      Name = name;
      Members = members;
    }

    public override string TypeName => "namespace";

    public override string ToString() => $"[namespace {Name}]";
  }
}
