using System.Collections;
using System.Collections.Generic;
using ALKScript.Interpreter.Common.Evaluation.Values;

namespace ALKScript.Interpreter.Common.Evaluation
{
  /// <summary>
  /// Named, read-only values the host seeds into a script's root environment
  /// before execution begins. Each entry is visible to the script as an
  /// ordinary variable that cannot be reassigned.
  ///
  /// <para>
  /// Supports collection-initializer syntax:
  /// <code>
  /// new ScriptArguments { ["entityId"] = new StringValue(id) }
  /// </code>
  /// </para>
  /// </summary>
  public sealed class ScriptArguments : IEnumerable<KeyValuePair<string, ALKScriptValue>>
  {
    private readonly Dictionary<string, ALKScriptValue> _args = new Dictionary<string, ALKScriptValue>();

    /// <summary>Sets the argument named <paramref name="name"/> to <paramref name="value"/>.</summary>
    public ALKScriptValue this[string name]
    {
      get => _args[name];
      set => _args[name] = value;
    }

    public int Count => _args.Count;

    public IEnumerator<KeyValuePair<string, ALKScriptValue>> GetEnumerator() => _args.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
  }
}
