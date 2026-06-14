using ALKScript.Interpreter.Common.Evaluation.Scheduling;
using ALKScript.Interpreter.Common.Modules;
using ALKScript.Interpreter.Evaluator.Cursor;

namespace ALKScript.Interpreter.Runtime
{
  /// <summary>
  /// The primary host-facing entry point for running ALKScript programs.
  /// Encapsulates the full pipeline — lexing, parsing, loading, and evaluation
  /// — behind straightforward run and load methods.
  ///
  /// Run methods return a <see cref="ProgramRun"/> handle. Drive a suspended
  /// run by inspecting <see cref="ProgramRun.PendingAwait"/> and calling
  /// <see cref="ProgramRun.Resume"/>/<see cref="ProgramRun.ResumeFaulted"/>, or
  /// block until completion with <see cref="ProgramRun.RunToCompletion"/>.
  ///
  /// <para>
  /// For scripts that are run repeatedly, use <see cref="LoadFromSource"/> or
  /// <see cref="LoadFromFile"/> to compile the program once into a
  /// <see cref="ModuleGraph"/>, cache the result, then start each run cheaply
  /// with <see cref="RunFromGraph"/> — skipping the lexing and parsing step
  /// every time.
  /// </para>
  /// </summary>
  public interface IProgramRuntime
  {
    /// <summary>
    /// Host implementation for <c>native async</c> function declarations.
    /// Set before calling any Run method; each <c>await</c> on a
    /// <see cref="PendingOperationValue"/> dispatches through this binder to
    /// start the host-side effect. Leave <c>null</c> if the program contains
    /// no <c>native async</c> declarations.
    /// </summary>
    IAsyncOperationBinder? OperationBinder { get; set; }

    /// <summary>
    /// Lexes and parses <paramref name="source"/> into a <see cref="ModuleGraph"/>
    /// without starting execution. Import declarations in the source are not
    /// supported in this mode — use <see cref="LoadFromFile"/> when the program
    /// spans multiple modules.
    /// </summary>
    ModuleGraph LoadFromSource(string source);

    /// <summary>
    /// Loads the program rooted at <paramref name="filePath"/> — resolving and
    /// parsing every module it (transitively) imports — into a
    /// <see cref="ModuleGraph"/> without starting execution.
    /// </summary>
    ModuleGraph LoadFromFile(string filePath);

    /// <summary>
    /// Begins executing a previously compiled <paramref name="graph"/>.
    /// The same graph can be passed to multiple concurrent calls; each call
    /// produces an independent <see cref="ProgramRun"/> with its own state.
    /// </summary>
    ProgramRun RunFromGraph(ModuleGraph graph);

    /// <summary>
    /// Lexes, parses, and begins executing <paramref name="source"/> as an
    /// ALKScript program. Equivalent to calling <see cref="LoadFromSource"/>
    /// followed by <see cref="RunFromGraph"/>. Import declarations in the
    /// source are not supported — use <see cref="RunFromFile"/> when the
    /// program spans multiple modules.
    /// </summary>
    ProgramRun RunFromSource(string source);

    /// <summary>
    /// Loads the program rooted at <paramref name="filePath"/> — resolving and
    /// parsing every module it (transitively) imports — then begins execution.
    /// Equivalent to calling <see cref="LoadFromFile"/> followed by
    /// <see cref="RunFromGraph"/>.
    /// </summary>
    ProgramRun RunFromFile(string filePath);
  }
}
