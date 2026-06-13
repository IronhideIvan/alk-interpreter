using System.Threading.Tasks;
using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Common.Evaluation.Scheduling;

namespace ALKScript.Interpreter.Common.Evaluation.Values
{
  /// <summary>
  /// The script-visible value a <c>thunk</c>/<c>thunk&lt;T&gt;</c>-returning
  /// <c>native</c> function call produces: a not-yet-started, "lazy/deferred
  /// start" awaitable (see docs/ASYNC_AWAIT_DESIGN.md's core requirements and
  /// <see cref="IAsyncOperationBinder"/>).
  ///
  /// Unlike <see cref="ThunkValue"/> — which always wraps an *already-running*
  /// (or already-completed) <see cref="System.Threading.Tasks.Task{TResult}"/>,
  /// the shape natives that resolve synchronously produce (those are
  /// eager-start, mirroring C#/JS) — this defers actually starting the
  /// underlying host-side effect until <see cref="Start"/> is first called:
  /// on `await` ("Suspend"), or at end-of-script for any operation nobody ever
  /// awaited ("Discard", decision #10). Calling the `native` function merely
  /// records the request; reporting "as seen from script" identically to
  /// <see cref="ThunkValue"/> (<see cref="TypeName"/> is the same `"thunk"`)
  /// is what lets `await` treat both uniformly.
  ///
  /// <see cref="Start"/> is memoized — calling it more than once (e.g. once
  /// from `await` and again from end-of-script "Discard" sweeping leftovers)
  /// returns the same task and starts the host effect at most once, which is
  /// what <see cref="IAsyncOperationBinder.Start"/>'s "invoked at most once
  /// per operation" guarantee relies on.
  /// </summary>
  public sealed class PendingOperationValue : ALKScriptValue
  {
    private readonly IAsyncOperationBinder _binder;
    private Task<ALKScriptValue>? _started;
    private bool _replayed;

    public PendingOperation Operation { get; }

    /// <summary>
    /// The "T" of the declared <c>thunk&lt;T&gt;</c> return type that produced
    /// this value, if any — used to validate the resolved result on
    /// <c>await</c>. <c>null</c> for a bare <c>thunk</c> (nothing to validate
    /// against).
    /// </summary>
    public TypeNode? ElementType { get; }

    public PendingOperationValue(PendingOperation operation, IAsyncOperationBinder binder, TypeNode? elementType = null)
    {
      Operation = operation;
      _binder = binder;
      ElementType = elementType;
    }

    /// <summary>
    /// Whether this operation is "consumed" — either started via <see cref="Start"/>
    /// or short-circuited via <see cref="MarkReplayed"/> during log replay —
    /// and therefore must not be passed to <see cref="IAsyncOperationBinder.Discard"/>
    /// at end-of-script.
    /// </summary>
    public bool HasStarted => _started != null || _replayed;

    /// <summary>
    /// Marks this operation as consumed by log replay — its result came from the
    /// recorded log, so the host-side effect must not be started again via
    /// <see cref="IAsyncOperationBinder.Discard"/> at end-of-script.
    /// </summary>
    public void MarkReplayed() => _replayed = true;

    /// <summary>
    /// Starts the underlying host-side effect via the <see cref="IAsyncOperationBinder"/>
    /// — exactly once, no matter how many times this is called — and returns
    /// the (possibly still-pending) task it settles with.
    /// </summary>
    public Task<ALKScriptValue> Start() => _started ??= _binder.Start(Operation);

    /// <summary>
    /// Marks this operation as already started with <paramref name="task"/> —
    /// used by "Phase B" structural Restore (docs/ASYNC_AWAIT_DESIGN.md
    /// Addendum 3, Step 14) when <see cref="IAsyncOperationBinder.Start"/> was
    /// already called directly to reissue a captured suspension, so
    /// <see cref="Start"/> doesn't call it again and <see cref="HasStarted"/>
    /// correctly reports <c>true</c> for end-of-script "Discard" sweeping.
    /// </summary>
    internal void MarkStarted(Task<ALKScriptValue> task) => _started = task;

    /// <summary>
    /// The task <see cref="Start"/> produced, if it has been called — without
    /// triggering <see cref="Start"/>'s side effect. Used by "Phase C"
    /// structural Capture (docs/ASYNC_AWAIT_DESIGN.md Addendum 3) to inspect
    /// whether this operation has settled, without starting it.
    /// </summary>
    internal Task<ALKScriptValue>? StartedTask => _started;

    public override string TypeName => "thunk";

    public override string ToString() =>
      _replayed ? "<task replayed>" :
      _started == null ? "<task not started>" :
      $"<task {(_started.IsCompleted ? "completed" : "pending")}>";
  }
}
