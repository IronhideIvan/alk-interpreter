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
  ///
  /// <para>
  /// Bindings may optionally be scoped to a module via the two-argument
  /// indexer <c>this[moduleSpecifier, name]</c>. When the evaluator resolves
  /// a native declaration it tries the module-qualified key first; if nothing
  /// is registered there it falls back to the unqualified key <c>this[name]</c>
  /// (stored internally as <c>(null, name)</c>). This lets two different core
  /// modules declare a same-named native function while each having its own,
  /// independent host implementation.
  /// </para>
  /// </summary>
  public sealed class ScriptNativeBindings : IReadOnlyDictionary<string, NativeFunctionImplementation>
  {
    private readonly Dictionary<(string? Module, string Name), NativeFunctionImplementation> _bindings;

    public ScriptNativeBindings()
    {
      _bindings = new Dictionary<(string?, string), NativeFunctionImplementation>();
    }

    public ScriptNativeBindings(ScriptNativeBindings bindings)
    {
      _bindings = new Dictionary<(string?, string), NativeFunctionImplementation>(bindings._bindings);
    }

    /// <summary>Registers (or replaces) the unqualified host implementation for <paramref name="name"/>. Used as a fallback when no module-qualified binding is found.</summary>
    public NativeFunctionImplementation this[string name]
    {
      get => _bindings[(null, name)];
      set => _bindings[(null, name)] = value;
    }

    /// <summary>Registers (or replaces) the host implementation for <paramref name="name"/> scoped to <paramref name="moduleSpecifier"/>.</summary>
    public NativeFunctionImplementation this[string moduleSpecifier, string name]
    {
      get => _bindings[(moduleSpecifier, name)];
      set => _bindings[(moduleSpecifier, name)] = value;
    }

    /// <summary>
    /// Looks up the implementation for <paramref name="name"/>, trying the
    /// module-qualified key <c>(moduleSpecifier, name)</c> first, then falling
    /// back to the unqualified key <c>(null, name)</c>.
    /// </summary>
    public bool TryGetValue(string? moduleSpecifier, string name, out NativeFunctionImplementation implementation)
    {
      if (moduleSpecifier != null && _bindings.TryGetValue((moduleSpecifier, name), out implementation!))
        return true;

      return _bindings.TryGetValue((null, name), out implementation!);
    }

    // IReadOnlyDictionary<string, ...> — surfaces only unqualified bindings; kept for
    // collection-initializer compat and ScriptNativeBindings(ScriptNativeBindings) copy.
    public IEnumerable<string> Keys
    {
      get
      {
        foreach (var key in _bindings.Keys)
          if (key.Module == null) yield return key.Name;
      }
    }

    public IEnumerable<NativeFunctionImplementation> Values
    {
      get
      {
        foreach (var entry in _bindings)
          if (entry.Key.Module == null) yield return entry.Value;
      }
    }

    public int Count => _bindings.Count;

    public bool ContainsKey(string key) => _bindings.ContainsKey((null, key));

    public bool TryGetValue(string key, out NativeFunctionImplementation value) => _bindings.TryGetValue((null, key), out value!);

    public IEnumerator<KeyValuePair<string, NativeFunctionImplementation>> GetEnumerator()
    {
      foreach (var entry in _bindings)
        if (entry.Key.Module == null)
          yield return new KeyValuePair<string, NativeFunctionImplementation>(entry.Key.Name, entry.Value);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
  }
}
