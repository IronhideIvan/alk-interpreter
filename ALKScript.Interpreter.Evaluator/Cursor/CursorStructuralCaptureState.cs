using System.Collections.Generic;

namespace ALKScript.Interpreter.Evaluator.Cursor
{
  /// <summary>
  /// A "Phase B" structural snapshot of a suspended <see cref="EvaluationCursor"/>
  /// run (docs/ASYNC_AWAIT_DESIGN.md Addendum 3): the live suspended trail and
  /// environment graph, serialized via <see cref="CapturedEnvironment"/>/
  /// <see cref="CapturedTrailEntry"/>/<see cref="CapturedHeapValue"/> rather
  /// than a replay log — giving O(1) <see cref="CursorProgramEvaluator.RestoreStructural"/>
  /// independent of the run's history length. Uses runtime
  /// <see cref="ALKScript.Interpreter.Common.Evaluation.Values.ALKScriptValue"/>/
  /// <see cref="ALKScript.Interpreter.Common.Ast.TypeNode"/> types directly —
  /// converting this to/from a wire format is
  /// <c>ALKScript.Interpreter.Serialization</c>'s responsibility (Step 15).
  /// </summary>
  public sealed class CursorStructuralCaptureState
  {
    /// <summary>
    /// The module whose <c>Program.Declarations</c> is the suspended run's
    /// root statement list — <c>"module:&lt;identifier&gt;"</c>, see
    /// <see cref="AstReference.ForModule"/>.
    /// </summary>
    public string ModuleKey { get; set; } = "";

    /// <summary>
    /// Heap objects (<c>InstanceValue</c>/<c>BaseValue</c>) reachable from
    /// <see cref="Environments"/>, addressed by index via
    /// <see cref="CapturedHeapValue.HeapRef"/> (Step 9).
    /// </summary>
    public List<CapturedHeapEntry> Heap { get; set; } = new();

    /// <summary>
    /// Snapshots of <c>ClassValue.StaticFields</c> for classes whose static
    /// fields were mutated and are reachable from <see cref="Heap"/>/
    /// <see cref="Environments"/> (Step 10).
    /// </summary>
    public List<CapturedClassStaticFields> StaticFields { get; set; } = new();

    public List<CapturedEnvironment> Environments { get; set; } = new();

    public List<CapturedTrailEntry> Trail { get; set; } = new();

    public int RootEnvironmentId { get; set; }

    public CapturedSignal? Signal { get; set; }

    public CapturedPendingAwait? PendingAwait { get; set; }
  }
}
