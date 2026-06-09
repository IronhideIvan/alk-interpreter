namespace ALKScript.Interpreter.Common.Modules
{
  /// <summary>
  /// Assembles the full module graph reachable from an entry source file:
  /// parses the entry module and recursively resolves, lexes, and parses
  /// every module it (transitively) imports, per §9.2 of the language spec.
  /// </summary>
  public interface IProgramLoader
  {
    /// <summary>
    /// Loads and returns the module graph rooted at <paramref name="entryFilePath"/>.
    /// Throws when the entry file cannot be found, or when an import within
    /// the graph cannot be resolved, forms a cycle, or names a declaration
    /// its target module does not export.
    /// </summary>
    ModuleGraph Load(string entryFilePath);

    /// <summary>
    /// Parses <paramref name="source"/> as the entry module and assembles a
    /// module graph from it, applying the same global prelude and core-module
    /// resolution as <see cref="Load"/>. Relative-path imports are not
    /// supported (there is no base directory to resolve against) and will
    /// throw; core-module imports resolve normally.
    /// </summary>
    ModuleGraph LoadFromSource(string source);
  }
}
