using System.Collections.Generic;

namespace ALKScript.Interpreter.Parser.Modules
{
  /// <summary>
  /// Supplies prelude source(s) for <see cref="ProgramLoader"/> to compile —
  /// each either a "true global" or a named, importable core module, per its
  /// <see cref="GlobalPreludeSource.ModuleName"/>:
  ///
  /// <list type="bullet">
  /// <item><description>
  /// Unnamed sources (<see cref="GlobalPreludeSource.Global"/>) are recorded
  /// on <see cref="Common.Modules.ModuleGraph.GlobalPreludes"/>, in order, so
  /// the evaluator executes their declarations into the root environment
  /// before the entry module runs — giving a runtime "zero-declaration"
  /// globals (e.g. a <c>print</c>) without any <c>import</c> or per-script
  /// re-declaration.
  /// </description></item>
  /// <item><description>
  /// Named sources (<see cref="GlobalPreludeSource.Module"/>) are folded into
  /// the module graph as §9.2 core modules, importable by their
  /// <see cref="GlobalPreludeSource.ModuleName"/> specifier (e.g.
  /// <c>import { HttpClient } from "http";</c>) but otherwise invisible —
  /// exactly as if an <see cref="ICoreModuleProvider"/> had supplied them.
  /// </description></item>
  /// </list>
  ///
  /// This mirrors <see cref="ICoreModuleProvider"/>: the loader owns
  /// "compile source -&gt; structured representation" for every kind of input
  /// it assembles into a <see cref="Common.Modules.ModuleGraph"/>, and the
  /// runtime supplies the content (here, raw source text, with an explicit
  /// choice of where each piece should live) through injection rather than
  /// the interpreter hardcoding any of it.
  /// </summary>
  public interface IGlobalPreludeProvider
  {
    /// <summary>
    /// The prelude sources to compile, in order. Unnamed entries share one
    /// root environment with each other and with the entry module; named
    /// entries become core modules under their respective specifiers. A
    /// runtime is free to mix both — e.g. a small always-on global prelude
    /// alongside a richer, import-only standard library.
    /// </summary>
    IReadOnlyList<GlobalPreludeSource> Sources { get; }
  }
}
