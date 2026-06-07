using System.Collections.Generic;
using ALKScript.Interpreter.Common.Ast;

namespace ALKScript.Interpreter.Parser.Modules
{
  /// <summary>
  /// Supplies the set of standard-library "core modules" (§9.2 — bare
  /// specifiers like "collections" or "http" that aren't resolved against the
  /// filesystem) and their parsed definitions, so <see cref="ProgramLoader"/>
  /// can fold them into the module graph exactly like file modules: confirm
  /// the specifier names a known module and validate named imports against
  /// what it exports. Implementations typically wrap pre-parsed, embedded
  /// ALKScript sources for the standard library; tests can supply a fake with
  /// an in-memory set of definitions.
  /// </summary>
  public interface ICoreModuleProvider
  {
    /// <summary>The specifiers of every core module this provider can supply (e.g. "collections", "http").</summary>
    IReadOnlyCollection<string> AvailableModules { get; }

    /// <summary>
    /// Returns the parsed definition of the core module named by
    /// <paramref name="specifier"/>. Only called for specifiers present in
    /// <see cref="AvailableModules"/>.
    /// </summary>
    ProgramNode GetModule(string specifier);
  }
}
