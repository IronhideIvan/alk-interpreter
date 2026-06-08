using System.Collections.Generic;
using ALKScript.Interpreter.Common.Evaluation.Values;

namespace ALKScript.Interpreter.Common.Evaluation
{
  /// <summary>
  /// A lexical scope mapping names to runtime values, optionally chained to
  /// an enclosing scope (e.g. a module's global scope enclosing a function's
  /// local scope, which a <see cref="Values.FunctionValue"/> captures as its closure).
  /// </summary>
  public class Environment
  {
    private readonly Environment? _enclosing;
    private readonly Dictionary<string, ALKScriptValue> _values = new Dictionary<string, ALKScriptValue>();

    public Environment(Environment? enclosing = null)
    {
      _enclosing = enclosing;
    }

    /// <summary>Binds <paramref name="name"/> to <paramref name="value"/> in this scope, shadowing any enclosing binding.</summary>
    public void Define(string name, ALKScriptValue value)
    {
      _values[name] = value;
    }

    /// <summary>Looks up <paramref name="name"/> in this scope or, failing that, any enclosing scope.</summary>
    public bool TryGet(string name, out ALKScriptValue value)
    {
      for (Environment? scope = this; scope != null; scope = scope._enclosing)
      {
        if (scope._values.TryGetValue(name, out value!))
        {
          return true;
        }
      }

      value = NullValue.Instance;
      return false;
    }

    /// <summary>
    /// Assigns to an existing binding of <paramref name="name"/> in this scope or
    /// an enclosing one. Returns false when no such binding exists.
    /// </summary>
    public bool TryAssign(string name, ALKScriptValue value)
    {
      for (Environment? scope = this; scope != null; scope = scope._enclosing)
      {
        if (scope._values.ContainsKey(name))
        {
          scope._values[name] = value;
          return true;
        }
      }

      return false;
    }
  }
}
