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
  }
}
