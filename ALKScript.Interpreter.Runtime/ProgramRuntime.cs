using ALKScript.Interpreter.Common;
using ALKScript.Interpreter.Common.Evaluation;
using ALKScript.Interpreter.Common.Evaluation.Scheduling;
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
  /// Wires together the lexer, parser, loader, and evaluator and exposes the
  /// two run methods declared on <see cref="IProgramRuntime"/> plus the
  /// <see cref="IScriptLoop"/> members needed to drive execution.
  ///
  /// <para>
  /// The zero-argument constructor is the intended path for most hosts: it
  /// creates the full default pipeline internally so the host needs nothing
  /// more than <c>new ProgramRuntime()</c>. The overloaded constructor accepts
  /// explicit dependencies for tests or advanced embeddings that need custom
  /// lexers, loaders, or evaluators.
  /// </para>
  /// </summary>
  public class ProgramRuntime : IProgramRuntime, IScriptLoop
  {
    private readonly IProgramLoader _loader;
    private readonly IEvaluator _evaluator;
    private readonly IScriptLoop _loop;

    /// <summary>
    /// Creates a runtime with the default pipeline: real filesystem module
    /// loading, no core modules, and a fresh <see cref="ScriptScheduler"/>.
    /// </summary>
    public ProgramRuntime()
    {
      var scheduler = new ScriptScheduler();
      _loop = scheduler;
      _evaluator = new ProgramEvaluator(scheduler: scheduler);
      _loader = new ProgramLoader(
        new ALKScriptLexer(),
        new ALKScriptParser(),
        new FileSystemModuleFileReader(),
        new EmptyCoreModuleProvider());
    }

    /// <summary>
    /// Creates a runtime with explicit dependencies — for tests or advanced
    /// embeddings that need to inject a custom loader or evaluator. The caller
    /// is responsible for providing the <paramref name="loop"/> that drives
    /// the evaluator's scheduler.
    /// </summary>
    public ProgramRuntime(IProgramLoader loader, IEvaluator evaluator, IScriptLoop loop)
    {
      _loader = loader;
      _evaluator = evaluator;
      _loop = loop;
    }

    /// <inheritdoc/>
    public ScriptEvaluation RunFromSource(string source)
    {
      var graph = _loader.LoadFromSource(source);
      return _evaluator.Evaluate(graph);
    }

    /// <inheritdoc/>
    public ScriptEvaluation RunFromFile(string filePath)
    {
      var graph = _loader.Load(filePath);
      return _evaluator.Evaluate(graph);
    }

    /// <inheritdoc/>
    public int Pump() => _loop.Pump();

    /// <inheritdoc/>
    public void RunUntilComplete(ScriptEvaluation evaluation) => _loop.RunUntilComplete(evaluation);
  }
}
