using System.Threading.Tasks;
using ALKScript.Interpreter.Common.Evaluation.Scheduling;

namespace ALKScript.Interpreter.Common.Evaluation.Values
{
  /// <summary>
  /// The script-visible value an <c>async native</c> function call produces:
  /// a not-yet-started, "lazy/deferred start" awaitable (see
  /// docs/ASYNC_AWAIT_DESIGN.md's core requirements and <see cref="IAsyncOperationBinder"/>).
  ///
  /// Unlike <see cref="TaskValue"/> — which always wraps an *already-running*
  /// (or already-completed) <see cref="System.Threading.Tasks.Task{TResult}"/>,
  /// the shape both natives that resolve synchronously and `async` *function*
  /// calls produce (those are eager-start, mirroring C#/JS) — this defers
  /// actually starting the underlying host-side effect until <see cref="Start"/>
  /// is first called: on `await` ("Suspend"), or at end-of-script for any
  /// operation nobody ever awaited ("Discard", decision #10). Calling the
  /// `async native` function merely records the request; reporting "as seen
  /// from script" identically to <see cref="TaskValue"/> (<see cref="TypeName"/>
  /// is the same `"Task"`) is what lets `await` treat both uniformly.
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

    public PendingOperationValue(PendingOperation operation, IAsyncOperationBinder binder)
    {
      Operation = operation;
      _binder = binder;
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

    public override string TypeName => "Task";

    public override string ToString() =>
      _replayed ? "<task replayed>" :
      _started == null ? "<task not started>" :
      $"<task {(_started.IsCompleted ? "completed" : "pending")}>";
  }
}
