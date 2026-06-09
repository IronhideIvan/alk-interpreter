using System.Collections.Generic;

namespace ALKScript.Interpreter.Runtime
{
  /// <summary>
  /// The host-registered core modules available to scripts as bare-specifier
  /// imports (e.g. <c>import { log } from "console"</c>).  Each entry maps a
  /// module specifier to the raw ALKScript source that declares it.
  ///
  /// A distinct type rather than a bare <see cref="Dictionary{TKey,TValue}"/>
  /// so the registration surface can grow independently — e.g. to carry
  /// pre-parsed caches, version metadata, or lazy-load callbacks — without
  /// changing every signature that accepts a module table.
  /// </summary>
  public sealed class ScriptCoreModules
  {
    private readonly Dictionary<string, string> _sources = new Dictionary<string, string>();

    /// <summary>
    /// Registers (or replaces) the ALKScript source for the core module
    /// identified by <paramref name="specifier"/>.
    /// </summary>
    public string this[string specifier]
    {
      get => _sources[specifier];
      set => _sources[specifier] = value;
    }

    /// <summary>The specifiers of every registered core module.</summary>
    public IEnumerable<string> Keys => _sources.Keys;

    /// <summary>Registers the ALKScript source for <paramref name="specifier"/>, throwing if it is already present.</summary>
    public void Add(string specifier, string source) => _sources.Add(specifier, source);

    /// <summary>Removes the core module registered under <paramref name="specifier"/>. Returns <c>true</c> if it was present.</summary>
    public bool Remove(string specifier) => _sources.Remove(specifier);

    /// <summary>Returns <c>true</c> if a core module is registered under <paramref name="specifier"/>.</summary>
    public bool ContainsKey(string specifier) => _sources.ContainsKey(specifier);

    public bool TryGetValue(string specifier, out string source)
      => _sources.TryGetValue(specifier, out source!);
  }
}
