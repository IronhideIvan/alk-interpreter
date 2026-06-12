using System;
using System.Collections.Generic;
using ALKScript.Interpreter.Common;
using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Common.Evaluation;
using ALKScript.Interpreter.Common.Evaluation.Scheduling;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Common.Modules;

namespace ALKScript.Interpreter.Evaluator.Cursor
{
  /// <summary>The outcome of <see cref="CursorProgramEvaluator.Evaluate"/>/<see cref="CursorProgramEvaluator.Resume"/>/<see cref="CursorProgramEvaluator.ResumeFaulted"/>.</summary>
  public enum ProgramRunResult
  {
    Completed,
    Awaiting,
  }

  /// <summary>
  /// A "Phase A" (replay-based) snapshot of a suspended
  /// <see cref="CursorProgramEvaluator"/> run (docs/ASYNC_AWAIT_DESIGN.md
  /// Addendum 3): the record-and-replay <see cref="Log"/> accumulated up to
  /// the point of suspension, plus the <see cref="Phase"/>/<see cref="ModuleIndex"/>
  /// the run had reached (informational only — <see cref="CursorProgramEvaluator.Restore"/>
  /// re-derives both by re-running <see cref="ModuleGraph.GlobalPreludes"/>
  /// and modules from the start with <see cref="Log"/> as the replay log).
  /// Uses runtime <see cref="OperationLogEntry"/>/<see cref="ALKScriptValue"/>
  /// types directly — converting this to/from a wire format is
  /// <c>ALKScript.Interpreter.Serialization</c>'s responsibility, not this
  /// project's.
  /// </summary>
  public sealed class CursorCaptureState
  {
    /// <summary>0 = was running global preludes, 1 = was running modules, 2 = finalizing. See <see cref="CursorProgramEvaluator"/>'s private <c>_phase</c>.</summary>
    public int Phase { get; }

    /// <summary>Index into the current phase's sequence at the point of suspension.</summary>
    public int ModuleIndex { get; }

    /// <summary>The record-and-replay log accumulated up to the point of suspension.</summary>
    public IReadOnlyList<OperationLogEntry> Log { get; }

    public CursorCaptureState(int phase, int moduleIndex, IReadOnlyList<OperationLogEntry> log)
    {
      Phase = phase;
      ModuleIndex = moduleIndex;
      Log = log;
    }
  }

  /// <summary>
  /// Cursor-evaluator counterpart to <see cref="ProgramEvaluator"/> (Step 8 of
  /// the cursor-rewrite plan): executes a <see cref="ModuleGraph"/> by running
  /// the runtime-supplied global preludes, then each module's top-level
  /// declarations in dependency order (each only once, base-of-the-dependency-
  /// graph first), synchronously via a single shared <see cref="EvaluationCursor"/>.
  ///
  /// Each prelude program's declarations, and each module's declarations, are
  /// run as one <see cref="EvaluationCursor.Start"/> "segment" — the only level
  /// at which <c>await</c> may suspend (per the cursor evaluator's current
  /// milestone). If a segment suspends, <see cref="Evaluate"/>/<see cref="Resume"/>
  /// return <see cref="ProgramRunResult.Awaiting"/> and the host resumes via
  /// <see cref="Resume"/>/<see cref="ResumeFaulted"/>; once that segment
  /// completes, traversal continues with the next segment exactly as
  /// <see cref="ProgramEvaluator.EvaluateCore"/>'s recursive <c>ExecuteModule</c>
  /// walk did.
  /// </summary>
  public sealed class CursorProgramEvaluator
  {
    private readonly EvaluationCursor _cursor;
    private readonly IFunctionValueFactory _functionValueFactory;

    private ModuleGraph? _graph;
    private ScriptEnvironment? _globals;
    private List<LoadedModule>? _moduleOrder;
    private Dictionary<string, ScriptEnvironment>? _moduleEnvs;

    /// <summary>0 = running global preludes, 1 = running modules (in <see cref="_moduleOrder"/> order), 2 = finalizing.</summary>
    private int _phase;

    /// <summary>Index into the current phase's sequence (<see cref="ModuleGraph.GlobalPreludes"/> or <see cref="_moduleOrder"/>).</summary>
    private int _index;

    public CursorProgramEvaluator(ScriptNativeBindings? nativeBindings = null, ScriptNativeMethodBindings? nativeMethodBindings = null, IAsyncOperationBinder? operationBinder = null, IReadOnlyList<OperationLogEntry>? replayLog = null)
      : this(new FunctionValueFactory(nativeBindings, nativeMethodBindings, operationBinder), replayLog)
    {
    }

    public CursorProgramEvaluator(IFunctionValueFactory functionValueFactory, IReadOnlyList<OperationLogEntry>? replayLog = null)
    {
      _functionValueFactory = functionValueFactory;
      _cursor = new EvaluationCursor(functionValueFactory, replayLog);
    }

    /// <summary>The ordered log of every <c>async native</c> operation outcome recorded during this run. See <see cref="ProgramEvaluator.Log"/>.</summary>
    public IReadOnlyList<OperationLogEntry> Log => _cursor.Log;

    /// <summary>What <see cref="Resume"/>/<see cref="ResumeFaulted"/> will settle, while <see cref="ProgramRunResult.Awaiting"/>.</summary>
    public AwaitHandle? PendingAwait => _cursor.PendingAwait;

    /// <summary>
    /// Begins evaluating <paramref name="graph"/>: seeds <see cref="ModuleGraph.GlobalPreludes"/>
    /// into the root environment, then runs each module reachable from
    /// <see cref="ModuleGraph.EntryModule"/> (via imports/re-exports, each
    /// only once) in dependency order.
    /// </summary>
    public ProgramRunResult Evaluate(ModuleGraph graph)
    {
      _graph = graph;
      _globals = new ScriptEnvironment();
      _moduleOrder = TopoOrder(graph);
      _moduleEnvs = new Dictionary<string, ScriptEnvironment>();
      _phase = 0;
      _index = 0;

      return Advance();
    }

    /// <summary>
    /// Captures a "Phase A" (replay-based) snapshot of this suspended run
    /// (docs/ASYNC_AWAIT_DESIGN.md Addendum 3) for later <see cref="Restore"/>.
    /// Only valid while <see cref="PendingAwait"/> is non-null.
    /// </summary>
    public CursorCaptureState Capture()
    {
      if (PendingAwait == null)
      {
        throw new InvalidOperationException("CursorProgramEvaluator.Capture called while not awaiting.");
      }

      return new CursorCaptureState(_phase, _index, _cursor.Capture());
    }

    /// <summary>
    /// Reconstructs a suspended run from <paramref name="state"/>: builds a
    /// fresh <see cref="CursorProgramEvaluator"/> seeded with
    /// <see cref="CursorCaptureState.Log"/> as its replay log, then runs
    /// <see cref="Evaluate"/> against <paramref name="graph"/> from the
    /// start. Every <c>await</c>/<c>whenAll</c> site that has a corresponding
    /// replay-log entry resolves instantly via the replay log instead of
    /// suspending, until the log is exhausted — at which point the run
    /// either suspends again at the same logical point as when
    /// <paramref name="state"/> was captured (returning
    /// <see cref="ProgramRunResult.Awaiting"/>, ready for
    /// <see cref="Resume"/>/<see cref="ResumeFaulted"/>), or, if
    /// <paramref name="state"/> was captured at the run's final suspension
    /// point, completes (<see cref="ProgramRunResult.Completed"/>) — both are
    /// valid outcomes.
    ///
    /// <paramref name="graph"/> must be an equivalent module graph to the one
    /// the original run was evaluating (rebuilt from the same source files/
    /// module identifiers) — <see cref="CursorCaptureState"/> does not
    /// capture the AST/module graph itself.
    /// </summary>
    public static CursorProgramEvaluator Restore(ModuleGraph graph, IFunctionValueFactory functionValueFactory, CursorCaptureState state, out ProgramRunResult result)
    {
      var evaluator = new CursorProgramEvaluator(functionValueFactory, state.Log);
      result = evaluator.Evaluate(graph);
      return evaluator;
    }

    /// <summary>As <see cref="Restore(ModuleGraph, IFunctionValueFactory, CursorCaptureState, out ProgramRunResult)"/>, constructing the <see cref="IFunctionValueFactory"/> from native bindings.</summary>
    public static CursorProgramEvaluator Restore(ModuleGraph graph, CursorCaptureState state, out ProgramRunResult result, ScriptNativeBindings? nativeBindings = null, ScriptNativeMethodBindings? nativeMethodBindings = null, IAsyncOperationBinder? operationBinder = null)
    {
      return Restore(graph, new FunctionValueFactory(nativeBindings, nativeMethodBindings, operationBinder), state, out result);
    }

    /// <summary>Resumes a suspended segment with the settled result of the pending <c>await</c>.</summary>
    public ProgramRunResult Resume(ALKScriptValue value)
    {
      var result = _cursor.Resume(value);
      if (result == RunResult.Awaiting) return ProgramRunResult.Awaiting;
      return AfterSegmentCompleted();
    }

    /// <summary>Resumes a suspended segment by raising <paramref name="faultMessage"/> as a thrown exception at the point of suspension.</summary>
    public ProgramRunResult ResumeFaulted(string faultMessage)
    {
      var result = _cursor.ResumeFaulted(faultMessage);
      if (result == RunResult.Awaiting) return ProgramRunResult.Awaiting;
      return AfterSegmentCompleted();
    }

    private ProgramRunResult Advance()
    {
      while (true)
      {
        RunResult stepResult;

        switch (_phase)
        {
          case 0:
            if (_index >= _graph!.GlobalPreludes.Count)
            {
              _cursor.Signal = null;
              _phase = 1;
              _index = 0;
              continue;
            }

            stepResult = _cursor.Start(_graph.GlobalPreludes[_index].Declarations, _globals!);
            break;

          case 1:
            if (_index >= _moduleOrder!.Count)
            {
              _phase = 2;
              continue;
            }

            var module = _moduleOrder[_index];
            var env = GetOrCreateModuleEnv(module);
            BindModuleDependencies(module, env);
            stepResult = _cursor.Start(module.Program.Declarations, env);
            break;

          default:
            Finish();
            return ProgramRunResult.Completed;
        }

        if (stepResult == RunResult.Awaiting)
        {
          return ProgramRunResult.Awaiting;
        }

        AdvanceAfterSegment();
      }
    }

    /// <summary>
    /// After a segment (prelude program or module's declarations) completes
    /// without suspending: if a <see cref="Signal"/> is pending, stops that
    /// phase early (matching the old evaluator's "if (_signal != null) return;"
    /// after each prelude/module); otherwise advances to the next segment.
    /// </summary>
    private void AdvanceAfterSegment()
    {
      if (_cursor.Signal != null)
      {
        if (_phase == 0)
        {
          // Skip remaining preludes; phase 0's loop head resets the signal and moves to phase 1.
          _index = _graph!.GlobalPreludes.Count;
        }
        else
        {
          // Stop module traversal entirely and finalize.
          _phase = 2;
        }
      }
      else
      {
        _index++;
      }
    }

    private ProgramRunResult AfterSegmentCompleted()
    {
      AdvanceAfterSegment();
      return Advance();
    }

    /// <summary>
    /// Fire-and-forget any async native operations that were called but never
    /// awaited (<see cref="IFunctionValueFactory.DiscardPending"/>, decision
    /// #10), then surface an uncaught <c>throw</c> as a <see cref="RuntimeException"/>.
    /// </summary>
    private void Finish()
    {
      if (_cursor.Signal?.Kind != SignalKind.Cancelled)
      {
        _functionValueFactory.DiscardPending(_ => { });
      }

      if (_cursor.Signal is { Kind: SignalKind.Thrown } thrown)
      {
        _cursor.Signal = null;
        throw new RuntimeException(
          AstTokenLocator.EndOfFile,
          $"Uncaught exception: {Operators.Stringify(thrown.Value)}");
      }

      // A stray top-level "return" simply ends the module's execution.
      _cursor.Signal = null;
    }

    /// <summary>
    /// Returns every module reachable from <see cref="ModuleGraph.EntryModule"/>
    /// (via "import" and re-export "from" declarations), each exactly once, in
    /// dependency order — every module a given module depends on appears
    /// before it. Equivalent to the old evaluator's recursive, memoized
    /// <c>ExecuteModule</c> walk, but computed up front since the dependency
    /// structure is static.
    /// </summary>
    private static List<LoadedModule> TopoOrder(ModuleGraph graph)
    {
      var order = new List<LoadedModule>();
      var visited = new HashSet<string>();

      void Visit(LoadedModule module)
      {
        if (!visited.Add(module.Identifier))
        {
          return;
        }

        foreach (var import in module.Program.Imports)
        {
          Visit(graph.Modules[module.ImportResolutions[import.Source.Lexeme]]);
        }

        foreach (var declaration in module.Program.Declarations)
        {
          if (declaration is ReExportDecl reExport)
          {
            Visit(graph.Modules[module.ImportResolutions[reExport.Source.Lexeme]]);
          }
        }

        order.Add(module);
      }

      Visit(graph.EntryModule);
      return order;
    }

    private ScriptEnvironment GetOrCreateModuleEnv(LoadedModule module)
    {
      if (!_moduleEnvs!.TryGetValue(module.Identifier, out var env))
      {
        env = new ScriptEnvironment(_globals);
        _moduleEnvs[module.Identifier] = env;
      }

      return env;
    }

    /// <summary>
    /// Binds the names <paramref name="module"/> brings in via "import" and
    /// re-export "from" declarations into <paramref name="env"/>. By the time
    /// this runs, every dependency has already completed its own segment
    /// (guaranteed by <see cref="TopoOrder"/>), so each dependency's
    /// environment is fully populated.
    /// </summary>
    private void BindModuleDependencies(LoadedModule module, ScriptEnvironment env)
    {
      foreach (var import in module.Program.Imports)
      {
        string resolvedId = module.ImportResolutions[import.Source.Lexeme];
        BindImport(import, _moduleEnvs![resolvedId], env);
      }

      foreach (var declaration in module.Program.Declarations)
      {
        if (declaration is ReExportDecl reExport)
        {
          string resolvedId = module.ImportResolutions[reExport.Source.Lexeme];
          BindNamedSpecifiers(reExport.Specifiers, _moduleEnvs![resolvedId], env);
        }
      }
    }

    /// <summary>Binds the names brought in by <paramref name="import"/> from <paramref name="sourceEnv"/> into <paramref name="targetEnv"/>.</summary>
    private static void BindImport(ImportDecl import, ScriptEnvironment sourceEnv, ScriptEnvironment targetEnv)
    {
      switch (import.Clause)
      {
        case NamedImportsClause namedImports:
          BindNamedSpecifiers(namedImports.Specifiers, sourceEnv, targetEnv);
          break;

        case NamespaceImportClause namespaceImport:
          var members = new Dictionary<string, ALKScriptValue>();
          foreach (var entry in sourceEnv.OwnBindings)
          {
            members[entry.Key] = entry.Value;
          }
          targetEnv.Define(namespaceImport.Alias.Lexeme, new NamespaceValue(namespaceImport.Alias.Lexeme, members));
          break;
      }
    }

    /// <summary>
    /// Binds each named specifier's value from <paramref name="sourceEnv"/>
    /// into <paramref name="targetEnv"/> under its local name (the alias, if
    /// any, otherwise the original name) — shared by named imports and
    /// re-export "from" clauses.
    /// </summary>
    private static void BindNamedSpecifiers(IReadOnlyList<ImportSpecifier> specifiers, ScriptEnvironment sourceEnv, ScriptEnvironment targetEnv)
    {
      foreach (var specifier in specifiers)
      {
        string exportedName = specifier.Name.Lexeme;
        string localName = specifier.Alias?.Lexeme ?? exportedName;

        if (!sourceEnv.TryGet(exportedName, out ALKScriptValue value))
        {
          throw new RuntimeException(specifier.Name, $"Module does not export '{exportedName}'.");
        }

        targetEnv.Define(localName, value);
      }
    }
  }
}
