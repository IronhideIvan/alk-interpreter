using System.Collections;
using System.Collections.Generic;

namespace ALKScript.Interpreter.Common.Evaluation.Values
{
  /// <summary>
  /// The host implementations for a script's <c>native</c> function/method
  /// declarations, keyed by declared name.
  ///
  /// A distinct type rather than a bare dictionary so the binding surface can
  /// grow independently — e.g. to carry additional host-registration concerns
  /// alongside the implementations — without changing every signature that
  /// accepts bindings.
  /// </summary>
  public sealed class ScriptNativeBindings : IReadOnlyDictionary<string, NativeFunctionImplementation>
  {
    private readonly Dictionary<string, NativeFunctionImplementation> _bindings;

    public ScriptNativeBindings()
    {
      _bindings = new Dictionary<string, NativeFunctionImplementation>();
    }

    public ScriptNativeBindings(IReadOnlyDictionary<string, NativeFunctionImplementation> bindings)
    {
      _bindings = new Dictionary<string, NativeFunctionImplementation>();

      foreach (var binding in bindings)
      {
        _bindings[binding.Key] = binding.Value;
      }
    }

    /// <summary>Registers (or replaces) the host implementation for <paramref name="name"/>.</summary>
    public NativeFunctionImplementation this[string name]
    {
      get => _bindings[name];
      set => _bindings[name] = value;
    }

    public IEnumerable<string> Keys => _bindings.Keys;

    public IEnumerable<NativeFunctionImplementation> Values => _bindings.Values;

    public int Count => _bindings.Count;

    public bool ContainsKey(string key) => _bindings.ContainsKey(key);

    public bool TryGetValue(string key, out NativeFunctionImplementation value) => _bindings.TryGetValue(key, out value!);

    public IEnumerator<KeyValuePair<string, NativeFunctionImplementation>> GetEnumerator() => _bindings.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
  }
}
