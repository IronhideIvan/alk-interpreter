using System.Collections.Generic;
using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Parser.Modules;

namespace ALKScript.Interpreter.Runtime
{
  /// <summary>
  /// A no-op <see cref="ICoreModuleProvider"/> used when the host supplies no
  /// standard library. Any <c>import</c> of a bare specifier will produce a
  /// "cannot find core module" error at load time, which is the correct
  /// behaviour for a runtime with no built-in modules.
  /// </summary>
  internal sealed class EmptyCoreModuleProvider : ICoreModuleProvider
  {
    public IReadOnlyCollection<string> AvailableModules { get; } = new string[0];

    public ProgramNode GetModule(string specifier)
    {
      throw new System.InvalidOperationException($"No core module '{specifier}' is registered.");
    }
  }
}
