using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Common.Evaluation.Scheduling;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Evaluator.Cursor
{
  /// <summary>
  /// One element of a composite <c>await [a, b, c]</c> suspension (see
  /// <see cref="AwaitHandle.ForComposite"/>). Exactly one of
  /// <see cref="Resolved"/>, <see cref="Task"/>, or <see cref="ReplayedFaultMessage"/>
  /// is set: <see cref="Resolved"/> for an element that was already a plain
  /// value or settled (successfully) via the replay log; <see cref="Task"/>
  /// for a live (possibly still-pending) operation — paired with
  /// <see cref="Operation"/> if it came from a <see cref="PendingOperationValue"/>
  /// (so its settled outcome can be recorded to the replay log);
  /// <see cref="ReplayedFaultMessage"/> for an element whose outcome was
  /// already replayed as a fault (already recorded, so it is not re-recorded
  /// or re-reported when resolved).
  /// </summary>
  public sealed class AwaitElement
  {
    public ALKScriptValue? Resolved { get; }

    public Task<ALKScriptValue>? Task { get; }

    public PendingOperation? Operation { get; }

    public TypeNode? ElementType { get; }

    public string? ReplayedFaultMessage { get; }

    /// <summary>
    /// The <see cref="PendingOperationValue"/>/<see cref="ThunkValue"/> this
    /// element's <see cref="Task"/> came from, if any — "Phase C" structural
    /// Capture/Restore (docs/ASYNC_AWAIT_DESIGN.md Addendum 3) uses this for
    /// reference-equality dedup against the same instance held in a local
    /// variable. <c>null</c> for <see cref="ForResolved"/>/<see cref="ForReplayedFault"/>
    /// elements (no live instance to dedup against).
    /// </summary>
    public ALKScriptValue? Source { get; }

    private AwaitElement(ALKScriptValue? resolved, Task<ALKScriptValue>? task, PendingOperation? operation, TypeNode? elementType, string? replayedFaultMessage, ALKScriptValue? source)
    {
      Resolved = resolved;
      Task = task;
      Operation = operation;
      ElementType = elementType;
      ReplayedFaultMessage = replayedFaultMessage;
      Source = source;
    }

    public static AwaitElement ForResolved(ALKScriptValue value, TypeNode? elementType) =>
      new AwaitElement(value, task: null, operation: null, elementType, replayedFaultMessage: null, source: null);

    public static AwaitElement ForTask(Task<ALKScriptValue> task, TypeNode? elementType, PendingOperation? operation = null, ALKScriptValue? source = null) =>
      new AwaitElement(resolved: null, task, operation, elementType, replayedFaultMessage: null, source);

    public static AwaitElement ForReplayedFault(string faultMessage, TypeNode? elementType) =>
      new AwaitElement(resolved: null, task: null, operation: null, elementType, faultMessage, source: null);

    /// <summary>Whether this element is a live operation that has not yet settled.</summary>
    public bool NeedsSuspend => Task != null && !Task.IsCompleted;
  }

  /// <summary>
  /// Identifies what a suspended <see cref="EvaluationCursor"/> is parked on
  /// when <see cref="StepResult.IsAwaiting"/> is true.
  ///
  /// For a single-element <c>await</c>, exactly one of <see cref="Operation"/>
  /// or <see cref="Task"/> is set: <see cref="Operation"/> for a not-yet-started
  /// <c>async native</c> (<see cref="PendingOperationValue"/>); <see cref="Task"/>
  /// for an already-started/completed operation (<see cref="ThunkValue"/>, or a
  /// <see cref="PendingOperationValue"/> once <see cref="PendingOperationValue.Start"/>
  /// has been called). <see cref="ElementType"/> carries the declared
  /// <c>thunk&lt;T&gt;</c> element type through to <see cref="EvaluationCursor.Resume"/>
  /// for <see cref="TypeChecking.MatchesType"/> validation.
  ///
  /// For a composite <c>await [a, b, c]</c> (see <see cref="ForComposite"/>),
  /// <see cref="CompositeElements"/> is set instead and <see cref="Operation"/>/
  /// <see cref="Task"/>/<see cref="ElementType"/> are all <c>null</c> — the host
  /// awaits <see cref="CompositeTask"/> (which settles only once every live
  /// element has completed, mirroring <see cref="Task.WhenAll"/>'s run-to-
  /// completion semantics) and then calls <see cref="EvaluationCursor.Resume"/>
  /// with any value (e.g. <see cref="NullValue.Instance"/>) — per-element
  /// results/faults are read directly off <see cref="CompositeElements"/>'
  /// stored tasks. <see cref="EvaluationCursor.ResumeFaulted"/> is not valid
  /// for a composite handle.
  /// </summary>
  public sealed class AwaitHandle
  {
    public PendingOperation? Operation { get; }

    public Task<ALKScriptValue>? Task { get; }

    public TypeNode? ElementType { get; }

    /// <summary>The <c>await</c> keyword token, used to report a type mismatch on <see cref="EvaluationCursor.Resume"/>.</summary>
    public ALKScriptToken Site { get; }

    /// <summary>Set only for a composite <c>await [a, b, c]</c> suspension; <c>null</c> for a single-element <c>await</c>.</summary>
    public IReadOnlyList<AwaitElement>? CompositeElements { get; }

    /// <summary>
    /// Set only for a composite <c>await [a, b, c]</c> suspension: a
    /// <see cref="Task.WhenAll"/> over every live element's task, which
    /// therefore settles (successfully or faulted) only once all of them have
    /// completed. The host awaits this and then calls <see cref="EvaluationCursor.Resume"/>.
    /// </summary>
    public Task? CompositeTask { get; }

    /// <summary>
    /// The <see cref="PendingOperationValue"/>/<see cref="ThunkValue"/> the
    /// single-element <c>await</c>'s operand evaluated to, if any. "Phase C"
    /// structural Capture/Restore (docs/ASYNC_AWAIT_DESIGN.md Addendum 3) uses
    /// this for reference-equality dedup against the same instance held in a
    /// local variable. <c>null</c> for a composite <c>await [a, b, c]</c>
    /// (see <see cref="AwaitElement.Source"/> instead).
    /// </summary>
    public ALKScriptValue? Source { get; }

    private AwaitHandle(PendingOperation? operation, Task<ALKScriptValue>? task, TypeNode? elementType, ALKScriptToken site, IReadOnlyList<AwaitElement>? compositeElements = null, Task? compositeTask = null, ALKScriptValue? source = null)
    {
      Operation = operation;
      Task = task;
      ElementType = elementType;
      Site = site;
      CompositeElements = compositeElements;
      CompositeTask = compositeTask;
      Source = source;
    }

    public static AwaitHandle ForTask(Task<ALKScriptValue> task, TypeNode? elementType, ALKScriptToken site, ALKScriptValue? source = null) =>
      new AwaitHandle(operation: null, task, elementType, site, source: source);

    public static AwaitHandle ForOperation(PendingOperation operation, TypeNode? elementType, ALKScriptToken site, ALKScriptValue? source = null) =>
      new AwaitHandle(operation, task: null, elementType, site, source: source);

    /// <summary>
    /// A <see cref="PendingOperationValue"/> whose <see cref="PendingOperationValue.Start"/>
    /// task did not complete synchronously — carries both <see cref="Task"/>
    /// (for the host to observe) and <see cref="Operation"/> (so
    /// <see cref="EvaluationCursor.Resume"/>/<see cref="EvaluationCursor.ResumeFaulted"/>
    /// can append the settled outcome to the replay log).
    /// </summary>
    public static AwaitHandle ForPendingTask(Task<ALKScriptValue> task, PendingOperation operation, TypeNode? elementType, ALKScriptToken site, ALKScriptValue? source = null) =>
      new AwaitHandle(operation, task, elementType, site, source: source);

    /// <summary>
    /// A composite <c>await [a, b, c]</c> where one or more <paramref name="elements"/>
    /// are live operations that have not yet settled.
    /// <see cref="CompositeTask"/> is a <see cref="Task.WhenAll"/> over those
    /// elements' tasks.
    /// </summary>
    public static AwaitHandle ForComposite(IReadOnlyList<AwaitElement> elements, ALKScriptToken site)
    {
      var liveTasks = elements.Where(e => e.Task != null).Select(e => e.Task!).ToArray();
      Task compositeTask = System.Threading.Tasks.Task.WhenAll(liveTasks);
      return new AwaitHandle(operation: null, task: null, elementType: null, site, compositeElements: elements, compositeTask: compositeTask);
    }
  }
}
