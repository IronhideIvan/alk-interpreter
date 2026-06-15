using System.Collections.Generic;
using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Common.Evaluation.Scheduling;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Evaluator.Cursor
{
  /// <summary>
  /// One element of a composite <c>await [a, b, c]</c> suspension (see
  /// <see cref="AwaitHandle.ForComposite"/>). Exactly one of
  /// <see cref="Resolved"/>, <see cref="Source"/>, or <see cref="ReplayedFaultMessage"/>
  /// is set: <see cref="Resolved"/> for an element that was already a plain
  /// value or settled (successfully) via the replay log; <see cref="Source"/>
  /// for a live <see cref="PendingOperationValue"/> — paired with
  /// <see cref="Operation"/> so its settled outcome can be recorded to the
  /// replay log; <see cref="ReplayedFaultMessage"/> for an element whose
  /// outcome was already replayed as a fault (already recorded, so it is not
  /// re-recorded or re-reported when resolved).
  /// </summary>
  public sealed class AwaitElement
  {
    public ALKScriptValue? Resolved { get; }

    public PendingOperation? Operation { get; }

    public TypeNode? ElementType { get; }

    public string? ReplayedFaultMessage { get; }

    /// <summary>
    /// The <see cref="PendingOperationValue"/>/<see cref="ThunkValue"/> this
    /// element came from, if any — "Phase C" structural Capture/Restore
    /// (docs/ASYNC_AWAIT_DESIGN.md Addendum 3) uses this for reference-equality
    /// dedup against the same instance held in a local variable. <c>null</c>
    /// for <see cref="ForResolved"/>/<see cref="ForReplayedFault"/> elements
    /// (no live instance to dedup against).
    /// </summary>
    public ALKScriptValue? Source { get; }

    private AwaitElement(ALKScriptValue? resolved, PendingOperation? operation, TypeNode? elementType, string? replayedFaultMessage, ALKScriptValue? source)
    {
      Resolved = resolved;
      Operation = operation;
      ElementType = elementType;
      ReplayedFaultMessage = replayedFaultMessage;
      Source = source;
    }

    public static AwaitElement ForResolved(ALKScriptValue value, TypeNode? elementType) =>
      new AwaitElement(value, operation: null, elementType, replayedFaultMessage: null, source: null);

    public static AwaitElement ForOperation(PendingOperationValue pending, TypeNode? elementType) =>
      new AwaitElement(resolved: null, pending.Operation, elementType, replayedFaultMessage: null, source: pending);

    public static AwaitElement ForReplayedFault(string faultMessage, TypeNode? elementType) =>
      new AwaitElement(resolved: null, operation: null, elementType, faultMessage, source: null);

    /// <summary>Whether this element is a live operation that has not yet settled.</summary>
    public bool NeedsSuspend => Source is PendingOperationValue pending && pending.Status is null or OperationStatus.Pending;
  }

  /// <summary>
  /// Identifies what a suspended <see cref="EvaluationCursor"/> is parked on
  /// when <see cref="StepResult.IsAwaiting"/> is true.
  ///
  /// For a single-element <c>await</c>, <see cref="Source"/> is the
  /// <see cref="PendingOperationValue"/> being awaited and <see cref="Operation"/>
  /// is its descriptor (for replay logging). <see cref="ElementType"/> carries
  /// the declared <c>thunk&lt;T&gt;</c> element type through to
  /// <see cref="EvaluationCursor.Resume"/> for <see cref="TypeChecking.MatchesType"/>
  /// validation.
  ///
  /// For a composite <c>await [a, b, c]</c> (see <see cref="ForComposite"/>),
  /// <see cref="CompositeElements"/> is set instead and <see cref="Operation"/>/
  /// <see cref="ElementType"/>/<see cref="Source"/> are all <c>null</c> — the
  /// host's "Pump" (see <c>ProgramRun.Pump</c>) polls each live element's
  /// <see cref="PendingOperationValue"/> and, once every element has settled,
  /// calls <see cref="EvaluationCursor.Resume"/> with any value (e.g.
  /// <see cref="NullValue.Instance"/>) — per-element results/faults are read
  /// directly off <see cref="CompositeElements"/>. <see cref="EvaluationCursor.ResumeFaulted"/>
  /// is not valid for a composite handle.
  /// </summary>
  public sealed class AwaitHandle
  {
    public PendingOperation? Operation { get; }

    public TypeNode? ElementType { get; }

    /// <summary>The <c>await</c> keyword token, used to report a type mismatch on <see cref="EvaluationCursor.Resume"/>.</summary>
    public ALKScriptToken Site { get; }

    /// <summary>Set only for a composite <c>await [a, b, c]</c> suspension; <c>null</c> for a single-element <c>await</c>.</summary>
    public IReadOnlyList<AwaitElement>? CompositeElements { get; }

    /// <summary>
    /// The <see cref="PendingOperationValue"/> the single-element <c>await</c>'s
    /// operand evaluated to. "Phase C" structural Capture/Restore
    /// (docs/ASYNC_AWAIT_DESIGN.md Addendum 3) uses this for reference-equality
    /// dedup against the same instance held in a local variable. <c>null</c>
    /// for a composite <c>await [a, b, c]</c> (see <see cref="AwaitElement.Source"/> instead).
    /// </summary>
    public ALKScriptValue? Source { get; }

    private AwaitHandle(PendingOperation? operation, TypeNode? elementType, ALKScriptToken site, IReadOnlyList<AwaitElement>? compositeElements = null, ALKScriptValue? source = null)
    {
      Operation = operation;
      ElementType = elementType;
      Site = site;
      CompositeElements = compositeElements;
      Source = source;
    }

    /// <summary>
    /// A single-element <c>await</c> parked on <paramref name="pending"/>,
    /// whose <see cref="PendingOperationValue.Status"/> is <c>null</c> or
    /// <see cref="OperationStatus.Pending"/>.
    /// </summary>
    public static AwaitHandle ForOperation(PendingOperationValue pending, TypeNode? elementType, ALKScriptToken site) =>
      new AwaitHandle(pending.Operation, elementType, site, source: pending);

    /// <summary>
    /// A composite <c>await [a, b, c]</c> where one or more <paramref name="elements"/>
    /// are live operations that have not yet settled (<see cref="AwaitElement.NeedsSuspend"/>).
    /// </summary>
    public static AwaitHandle ForComposite(IReadOnlyList<AwaitElement> elements, ALKScriptToken site) =>
      new AwaitHandle(operation: null, elementType: null, site, compositeElements: elements);
  }
}
