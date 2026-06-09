using ALKScript.Interpreter.Common.Evaluation.Scheduling;

namespace ALKScript.Interpreter.Common
{
  /// <summary>
  /// The primary host-facing entry point for running ALKScript programs.
  /// Encapsulates the full pipeline — lexing, parsing, loading, and evaluation
  /// — behind two straightforward run methods.
  ///
  /// Both methods return a <see cref="ScriptEvaluation"/> handle. Drive
  /// execution by passing it to <see cref="IScriptLoop.RunUntilComplete"/> for
  /// blocking/"run and wait" scenarios, or by calling
  /// <see cref="IScriptLoop.Pump"/> each game-loop tick and checking
  /// <see cref="ScriptEvaluation.IsCompleted"/> to detect when the script has
  /// finished.
  /// </summary>
  public interface IProgramRuntime
  {
    /// <summary>
    /// Lexes, parses, and begins executing <paramref name="source"/> as an
    /// ALKScript program. Import declarations in the source are not supported
    /// in this mode — use <see cref="RunFromFile"/> when the program spans
    /// multiple modules.
    /// </summary>
    ScriptEvaluation RunFromSource(string source);

    /// <summary>
    /// Loads the program rooted at <paramref name="filePath"/> — resolving and
    /// parsing every module it (transitively) imports — then begins execution.
    /// </summary>
    ScriptEvaluation RunFromFile(string filePath);
  }
}
