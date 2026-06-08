using System.Collections.Generic;

namespace ALKScript.Interpreter.Evaluator
{
  /// <summary>
  /// A lexical scope mapping names to runtime values, optionally chained to
  /// an enclosing scope (e.g. a module's global scope enclosing a function's
  /// local scope). Acts as a placeholder for the runtime value model, which
  /// has not been designed yet.
  /// </summary>
  public class Environment
  {
    private readonly Environment? _enclosing;
    private readonly Dictionary<string, object?> _values = new Dictionary<string, object?>();

    public Environment(Environment? enclosing = null)
    {
      _enclosing = enclosing;
    }
  }
}
