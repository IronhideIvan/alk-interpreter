namespace ALKScript.Interpreter.Parser.Modules
{
  /// <summary>
  /// One source supplied by an <see cref="IGlobalPreludeProvider"/>, paired
  /// with where its declarations should land:
  ///
  /// <list type="bullet">
  /// <item><description>
  /// <see cref="ModuleName"/> is <c>null</c> — the source is a "true global"
  /// prelude: <see cref="ProgramLoader"/> compiles it and records it on
  /// <see cref="Common.Modules.ModuleGraph.GlobalPreludes"/>, so the evaluator
  /// executes its declarations into the root environment before the entry
  /// module runs (visible everywhere, no <c>import</c> needed).
  /// </description></item>
  /// <item><description>
  /// <see cref="ModuleName"/> names a module specifier (e.g. <c>"http"</c>) —
  /// the source instead becomes an importable §9.2 core module under that
  /// name, exactly like one supplied by an <c>ICoreModuleProvider</c>:
  /// <c>import { HttpClient } from "http";</c> resolves to it, but it has no
  /// presence in the global scope unless imported.
  /// </description></item>
  /// </list>
  ///
  /// This lets a single runtime-supplied source list describe both "things
  /// every script can already see" and "things a script must opt into via
  /// import" — e.g. a small always-on prelude (<c>print</c>, <c>assert</c>)
  /// alongside a richer standard library that stays out of the way until
  /// imported.
  /// </summary>
  public readonly struct GlobalPreludeSource
  {
    /// <summary>
    /// The module specifier this source should be importable as, or
    /// <c>null</c> when it should instead be seeded directly into the root
    /// environment as a true global. Never empty — use <c>null</c> (e.g. via
    /// <see cref="Global"/>) to mean "no namespace".
    /// </summary>
    public string? ModuleName { get; }

    /// <summary>The ALKScript source text to compile.</summary>
    public string Source { get; }

    private GlobalPreludeSource(string? moduleName, string source)
    {
      ModuleName = moduleName;
      Source = source;
    }

    /// <summary>
    /// A "true global" prelude source: its declarations are executed straight
    /// into the root environment, with no module identity of their own.
    /// </summary>
    public static GlobalPreludeSource Global(string source) => new GlobalPreludeSource(null, source);

    /// <summary>
    /// A source that should be importable as the core module
    /// <paramref name="moduleName"/> (e.g. <c>"http"</c>) rather than seeded
    /// into the global scope — see §9.2 of the language spec.
    /// </summary>
    public static GlobalPreludeSource Module(string moduleName, string source) => new GlobalPreludeSource(moduleName, source);
  }
}
