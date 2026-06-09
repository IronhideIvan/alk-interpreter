using ALKScript.Interpreter.Common;
using ALKScript.Interpreter.Common.Evaluation;
using ALKScript.Interpreter.Common.Evaluation.Scheduling;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Common.Modules;
using ALKScript.Interpreter.Evaluator;
using ALKScript.Interpreter.Evaluator.Scheduling;
using ALKScript.Interpreter.Lexer;
using ALKScript.Interpreter.Parser;
using ALKScript.Interpreter.Parser.Modules;

namespace ALKScript.Interpreter.Runtime
{
  /// <summary>
  /// The primary host-facing entry point for running ALKScript programs.
  /// Acts as an orchestrator: multiple scripts — and multiple concurrent
  /// evaluation instances of the same script — can be started at any time,
  /// including between game ticks. All active evaluations share one scheduler,
  /// so a single <see cref="Pump"/> call advances every script that has work
  /// ready during that tick.
  ///
  /// <para>
  /// Register host implementations for <c>native</c> function and method
  /// declarations via <see cref="NativeBindings"/> and
  /// <see cref="NativeMethodBindings"/> before running scripts. Both tables
  /// are mutable — entries can be added at any time and will be picked up by
  /// the next <see cref="RunFromSource"/> or <see cref="RunFromFile"/> call.
  /// </para>
  ///
  /// <para>
  /// The zero-argument constructor is the intended path for most hosts: it
  /// creates the full default pipeline internally so the host needs nothing
  /// more than <c>new ProgramRuntime()</c>. The overloaded constructor accepts
  /// explicit dependencies for tests or advanced embeddings that need a custom
  /// loader, scheduler, or evaluator factory.
  /// </para>
  /// </summary>
  public class ProgramRuntime : IProgramRuntime, IScriptLoop
  {
    private readonly IProgramLoader _loader;
    private readonly IScriptScheduler _scheduler;
    private readonly IScriptLoop _loop;
    private readonly IEvaluatorFactory _factory;

    /// <summary>
    /// Host implementations for free-standing <c>native function</c>
    /// declarations, keyed by declared name. Populate before calling
    /// <see cref="RunFromSource"/> or <see cref="RunFromFile"/>.
    /// </summary>
    public ScriptNativeBindings NativeBindings { get; } = new ScriptNativeBindings();

    /// <summary>
    /// Host implementations for <c>native</c> method declarations, keyed by
    /// declaring class name and member name. Populate before calling
    /// <see cref="RunFromSource"/> or <see cref="RunFromFile"/>.
    /// </summary>
    public ScriptNativeMethodBindings NativeMethodBindings { get; } = new ScriptNativeMethodBindings();

    /// <summary>
    /// Creates a runtime with the default pipeline: real filesystem module
    /// loading, no core modules, and a fresh <see cref="ScriptScheduler"/>.
    /// </summary>
    public ProgramRuntime()
    {
      var scheduler = new ScriptScheduler();
      _scheduler = scheduler;
      _loop = scheduler;
      _factory = new EvaluatorFactory();
      _loader = new ProgramLoader(
        new ALKScriptLexer(),
        new ALKScriptParser(),
        new FileSystemModuleFileReader(),
        new EmptyCoreModuleProvider());
    }

    /// <summary>
    /// Creates a runtime with explicit dependencies — for tests or advanced
    /// embeddings that need to inject a custom loader, scheduler, or evaluator
    /// factory. <paramref name="scheduler"/> and <paramref name="loop"/> should
    /// typically be the same <see cref="ScriptScheduler"/> instance, since the
    /// loop must drain the continuations that the scheduler enqueues.
    /// </summary>
    public ProgramRuntime(
      IProgramLoader loader,
      IScriptScheduler scheduler,
      IScriptLoop loop,
      IEvaluatorFactory factory)
    {
      _loader = loader;
      _scheduler = scheduler;
      _loop = loop;
      _factory = factory;
    }

    /// <inheritdoc/>
    public ModuleGraph LoadFromSource(string source) => _loader.LoadFromSource(source);

    /// <inheritdoc/>
    public ModuleGraph LoadFromFile(string filePath) => _loader.Load(filePath);

    /// <inheritdoc/>
    public ScriptEvaluation RunFromGraph(ModuleGraph graph) =>
      _factory.Create(_scheduler, NativeBindings, NativeMethodBindings).Evaluate(graph);

    /// <inheritdoc/>
    public ScriptEvaluation RunFromSource(string source) => RunFromGraph(LoadFromSource(source));

    /// <inheritdoc/>
    public ScriptEvaluation RunFromFile(string filePath) => RunFromGraph(LoadFromFile(filePath));

    /// <inheritdoc/>
    public int Pump() => _loop.Pump();

    /// <inheritdoc/>
    public void RunUntilComplete(ScriptEvaluation evaluation) => _loop.RunUntilComplete(evaluation);
  }
}
