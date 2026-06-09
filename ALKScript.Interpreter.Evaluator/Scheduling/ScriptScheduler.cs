using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using ALKScript.Interpreter.Common.Evaluation.Scheduling;

namespace ALKScript.Interpreter.Evaluator.Scheduling
{
  /// <summary>
  /// The single-threaded "host thread" scheduler — see
  /// docs/ASYNC_AWAIT_DESIGN.md decision #2.
  ///
  /// Continuation routing no longer relies on
  /// <see cref="SynchronizationContext"/>. Instead, every <c>await</c> inside
  /// the evaluator uses a <c>ScheduledOn(scheduler)</c> wrapper (see
  /// <see cref="ScheduledTask{T}"/>) whose <c>OnCompleted</c> calls
  /// <see cref="Enqueue"/> directly. That makes the scheduler fully scoped to
  /// the evaluation — no thread-local global state to install or restore.
  ///
  /// Queued work runs in <b>deterministic, stable (insertion) order</b> —
  /// decision #7: "guaranteeing this now is cheap; retrofitting it later,
  /// once content has come to depend on incidental ordering, would be
  /// expensive and likely breaking."
  /// </summary>
  public sealed class ScriptScheduler : IScriptLoop, IScriptScheduler
  {
    private readonly ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();
    private readonly SemaphoreSlim _workAvailable = new SemaphoreSlim(0);

    /// <inheritdoc/>
    public void Enqueue(Action continuation)
    {
      _queue.Enqueue(continuation);
      _workAvailable.Release();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// This is the "pumped once per game-loop tick" contract from decision
    /// #2/#6: it's what gives "after pause(1), the script resumes seeing game
    /// state as of the next completed simulation step" its precise meaning —
    /// "next <c>Pump()</c>", not "later in this one".
    /// </remarks>
    public int Pump()
    {
      int due = _queue.Count;
      int ran = 0;

      while (ran < due && _queue.TryDequeue(out var action))
      {
        action();
        ran++;
      }

      return ran;
    }

    /// <summary>
    /// Runs this scheduler until <paramref name="task"/> completes, pumping
    /// continuations as they arrive and otherwise waiting (rather than
    /// busy-spinning) for the next one. Not part of <see cref="IScriptScheduler"/>:
    /// a real game host owns its own loop and drives ticks itself via
    /// <see cref="Pump"/>. This is the convenience an embedding <em>without</em>
    /// such a loop (tests; "run this script and give me the result") needs.
    ///
    /// Rethrows any exception <paramref name="task"/> faulted with (unwrapped,
    /// like <c>await</c> would).
    /// </summary>
    public void RunUntilComplete(ScriptEvaluation evaluation)
    {
      var task = evaluation.Task;
      // Drain the queue until the top-level task is complete AND no further
      // continuations are pending. Checking "queue empty" before exiting is
      // necessary because fire-and-forget async functions (e.g. a script that
      // calls "main()" without awaiting it) enqueue their continuations via
      // Enqueue() while the top-level Evaluate task may already be complete —
      // those continuations must still run to completion before we return.
      while (true)
      {
        if (_queue.TryDequeue(out var action))
        {
          action();
        }
        else if (task.IsCompleted)
        {
          break;
        }
        else
        {
          // Wakes as soon as Enqueue() adds new work; the bounded wait is
          // just a safety net — it costs nothing when work is flowing.
          _workAvailable.Wait(millisecondsTimeout: 15);
        }
      }

      task.GetAwaiter().GetResult();
    }
  }
}
