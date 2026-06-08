using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using ALKScript.Interpreter.Common.Evaluation.Scheduling;

namespace ALKScript.Interpreter.Evaluator.Scheduling
{
  /// <summary>
  /// The single-threaded "host thread" every script's <c>await</c> resumes
  /// on — see docs/ASYNC_AWAIT_DESIGN.md decision #2.
  ///
  /// Rejected thread-per-script (too costly for "potentially many scripts
  /// awaiting"). Instead, this is a custom <see cref="SynchronizationContext"/>:
  /// installing it as <see cref="SynchronizationContext.Current"/> before
  /// running a script causes every <c>await</c> continuation anywhere in that
  /// script's call tree — no matter which background thread the awaited
  /// operation actually settles on — to be <see cref="Post"/>ed back here
  /// instead of resuming inline on that background thread. That's what
  /// makes "the script and the game loop never run concurrently" hold even
  /// though the underlying .NET <see cref="Task"/> machinery is fully
  /// multithreaded: nothing related to a script ever *runs* except while this
  /// scheduler is pumping it, on the one thread that does so.
  ///
  /// Queued work runs in **deterministic, stable (insertion) order** — see
  /// decision #7: "guaranteeing this now is cheap; retrofitting it later,
  /// once content has come to depend on incidental ordering, would be
  /// expensive and likely breaking."
  /// </summary>
  public sealed class ScriptScheduler : SynchronizationContext, IScriptScheduler
  {
    private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new ConcurrentQueue<(SendOrPostCallback, object?)>();
    private readonly SemaphoreSlim _workAvailable = new SemaphoreSlim(0);

    /// <summary>
    /// Queues <paramref name="callback"/> to run on a future <see cref="Pump"/>
    /// (or <see cref="RunUntilComplete"/> iteration) — never inline, and never
    /// on the calling (possibly background) thread. This is what every
    /// <c>await</c> continuation in a script funnels through once this
    /// scheduler is installed as <see cref="SynchronizationContext.Current"/>.
    /// </summary>
    public override void Post(SendOrPostCallback callback, object? state)
    {
      _queue.Enqueue((callback, state));
      _workAvailable.Release();
    }

    /// <summary>
    /// Cooperative single-threaded model — there is never a genuinely
    /// different thread to hand off to and wait on, so this simply runs
    /// <paramref name="callback"/> immediately, on the calling thread.
    /// </summary>
    public override void Send(SendOrPostCallback callback, object? state) => callback(state);

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

      while (ran < due && _queue.TryDequeue(out var item))
      {
        item.Callback(item.State);
        ran++;
      }

      return ran;
    }

    /// <summary>
    /// Runs this scheduler — as <see cref="SynchronizationContext.Current"/>
    /// on the calling thread — until <paramref name="task"/> completes,
    /// pumping continuations as they arrive and otherwise waiting (rather
    /// than busy-spinning) for the next one. Not part of <see cref="IScriptScheduler"/>:
    /// a real game host owns its own loop and drives ticks itself via
    /// <see cref="Pump"/>, so a script only ever resumes between simulation
    /// steps. This is instead the convenience an embedding *without* such a
    /// loop (tests; "run this script and give me the result") needs to drive
    /// a script through any number of real suspensions to completion on one
    /// thread.
    ///
    /// Restores the previous <see cref="SynchronizationContext.Current"/>
    /// before returning, and rethrows any exception <paramref name="task"/>
    /// faulted with (unwrapped, like <c>await</c> would).
    /// </summary>
    public void RunUntilComplete(Task task)
    {
      var previous = Current;
      SetSynchronizationContext(this);

      try
      {
        while (!task.IsCompleted)
        {
          if (_queue.TryDequeue(out var item))
          {
            item.Callback(item.State);
          }
          else
          {
            // Wakes as soon as Post() queues new work; the bounded wait is
            // just a safety net against a missed/extra Release() ever
            // stalling completion — it costs nothing when work is flowing.
            _workAvailable.Wait(millisecondsTimeout: 15);
          }
        }
      }
      finally
      {
        SetSynchronizationContext(previous);
      }

      task.GetAwaiter().GetResult();
    }
  }
}
