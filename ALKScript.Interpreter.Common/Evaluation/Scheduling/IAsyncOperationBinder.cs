using System;
using System.Threading.Tasks;
using ALKScript.Interpreter.Common.Evaluation.Values;

namespace ALKScript.Interpreter.Common.Evaluation.Scheduling
{
  /// <summary>
  /// The host-integration contract for <c>thunk</c> native operations — see
  /// docs/ASYNC_AWAIT_DESIGN.md's "Host Integration Guide" companion (decision
  /// #8). A host implements this once (typically dispatching on
  /// <see cref="PendingOperation.Name"/>, e.g. via a <c>switch</c>) to wire
  /// every <c>thunk</c>-returning <c>native</c> declaration to the real,
  /// host-side game effect it represents — replacing the per-declaration
  /// delegate registration (<see cref="ScriptNativeBindings"/>) that ordinary,
  /// synchronous <c>native</c>s use, because *when* the operation starts is no
  /// longer simply "when it's called" (see "lazy/deferred start" below).
  ///
  /// Calling a <c>thunk</c>-returning <c>native</c> function produces a
  /// <see cref="PendingOperationValue"/> immediately — without invoking this
  /// contract — recording only *that* the operation was requested, with what
  /// arguments. <see cref="Start"/> is invoked at most once per operation,
  /// lazily, the moment (and only if) the operation actually needs to run for
  /// real:
  ///
  /// - on `await` of the resulting value ("Suspend" — see <c>ExpressionEvaluator.EvalAwait</c>), or
  /// - at end-of-script, for any operation that was created but never `await`ed
  ///   ("Discard", decision #10 — fire-and-forget; not yet implemented).
  ///
  /// This non-redundant "or" is what makes fire-and-forget possible: an
  /// un-awaited `moveTo(npc, x, y)` still moves the NPC, exactly once, started
  /// at the moment script execution ends rather than when it was called — see
  /// the design doc's core-requirements section for why this is load-bearing,
  /// not incidental.
  /// </summary>
  public interface IAsyncOperationBinder
  {
    /// <summary>
    /// Starts the host-side effect <paramref name="operation"/> describes, and
    /// returns a task that completes with its result (or fault) once the
    /// effect finishes — possibly many game-loop ticks later. Invoked at most
    /// once per <see cref="PendingOperation"/> (see <see cref="PendingOperationValue.Start"/>),
    /// so an implementation never needs to guard against being asked to start
    /// the same operation twice.
    /// </summary>
    Task<ALKScriptValue> Start(PendingOperation operation);

    /// <summary>
    /// Called at end-of-script for any <c>thunk</c> native operation that was
    /// called but never <c>await</c>ed — the "fire-and-forget" path that makes
    /// an un-awaited <c>moveTo(npc, x, y)</c> actually move the NPC (see
    /// docs/ASYNC_AWAIT_DESIGN.md's core requirements). The host is responsible
    /// for starting the operation and feeding any eventual fault back through
    /// <paramref name="onFault"/>; the framework guarantees
    /// <see cref="PendingOperationValue.Start"/> has not yet been called, so
    /// the host is the first (and only) caller.
    /// </summary>
    void Discard(PendingOperation operation, Action<Exception> onFault);

    /// <summary>
    /// Called for each individually-faulted member of a <c>await [a, b, …]</c>
    /// (see decision #11) — in addition to, not instead of, the aggregate fault
    /// the script can <c>catch</c>. Use this for host-side logging or telemetry;
    /// throwing here has no effect on script-visible behavior.
    /// </summary>
    void OnOperationFaulted(PendingOperation operation, Exception fault);
  }
}
