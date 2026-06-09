using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using ALKScript.Interpreter.Common.Evaluation.Scheduling;

namespace ALKScript.Interpreter.Evaluator.Scheduling
{
  /// <summary>
  /// Extension methods that wrap a <see cref="Task{T}"/> or <see cref="Task"/>
  /// in a custom awaitable whose continuation is enqueued directly onto
  /// <paramref name="scheduler"/> via <see cref="IScriptScheduler.Enqueue"/>
  /// rather than being routed through <see cref="System.Threading.SynchronizationContext"/>.
  ///
  /// This is the mechanism that lets <see cref="ScriptScheduler"/> work without
  /// being installed as <c>SynchronizationContext.Current</c> — every
  /// externally-sourced <c>Task</c> the evaluator awaits (host-binder results,
  /// <c>Task.WhenAll</c>, user async-function tasks) uses this wrapper, so
  /// continuations are always routed to the right scheduler instance regardless
  /// of what the ambient context is.
  ///
  /// When <paramref name="scheduler"/> is <c>null</c> the wrapper degrades
  /// gracefully to the default <see cref="TaskAwaiter"/> behaviour, preserving
  /// backward compatibility for embeddings that don't supply a scheduler.
  /// </summary>
  internal static class ScheduledTaskExtensions
  {
    internal static ScheduledTask<T> ScheduledOn<T>(this Task<T> task, IScriptScheduler? scheduler)
      => new ScheduledTask<T>(task, scheduler);

    internal static ScheduledTask ScheduledOn(this Task task, IScriptScheduler? scheduler)
      => new ScheduledTask(task, scheduler);
  }

  // ---------------------------------------------------------------------------
  // Task<T> variant
  // ---------------------------------------------------------------------------

  internal readonly struct ScheduledTask<T>
  {
    private readonly Task<T> _task;
    private readonly IScriptScheduler? _scheduler;

    internal ScheduledTask(Task<T> task, IScriptScheduler? scheduler)
    {
      _task = task;
      _scheduler = scheduler;
    }

    public ScheduledTaskAwaiter<T> GetAwaiter() => new ScheduledTaskAwaiter<T>(_task, _scheduler);
  }

  internal readonly struct ScheduledTaskAwaiter<T> : ICriticalNotifyCompletion
  {
    private readonly Task<T> _task;
    private readonly IScriptScheduler? _scheduler;

    internal ScheduledTaskAwaiter(Task<T> task, IScriptScheduler? scheduler)
    {
      _task = task;
      _scheduler = scheduler;
    }

    public bool IsCompleted => _task.IsCompleted;

    public T GetResult() => _task.GetAwaiter().GetResult();

    public void OnCompleted(Action continuation)
    {
      // Copy fields to locals — struct lambdas cannot capture 'this'.
      var scheduler = _scheduler;
      var task = _task;
      if (scheduler != null)
        // ExecuteSynchronously: Enqueue runs inline on the completing thread
        // (inside SetResult for a TCS, or on the background thread for Task.Run)
        // so the continuation is always in the queue before the caller proceeds.
        task.ContinueWith(
          _ => scheduler.Enqueue(continuation),
          System.Threading.CancellationToken.None,
          TaskContinuationOptions.ExecuteSynchronously,
          TaskScheduler.Default);
      else
        task.GetAwaiter().OnCompleted(continuation);
    }

    // The C# compiler calls UnsafeOnCompleted (not OnCompleted) for async
    // state machines. Forwarding here keeps the semantics identical.
    public void UnsafeOnCompleted(Action continuation) => OnCompleted(continuation);
  }

  // ---------------------------------------------------------------------------
  // Task (void) variant
  // ---------------------------------------------------------------------------

  internal readonly struct ScheduledTask
  {
    private readonly Task _task;
    private readonly IScriptScheduler? _scheduler;

    internal ScheduledTask(Task task, IScriptScheduler? scheduler)
    {
      _task = task;
      _scheduler = scheduler;
    }

    public ScheduledTaskAwaiter GetAwaiter() => new ScheduledTaskAwaiter(_task, _scheduler);
  }

  internal readonly struct ScheduledTaskAwaiter : ICriticalNotifyCompletion
  {
    private readonly Task _task;
    private readonly IScriptScheduler? _scheduler;

    internal ScheduledTaskAwaiter(Task task, IScriptScheduler? scheduler)
    {
      _task = task;
      _scheduler = scheduler;
    }

    public bool IsCompleted => _task.IsCompleted;

    public void GetResult() => _task.GetAwaiter().GetResult();

    public void OnCompleted(Action continuation)
    {
      var scheduler = _scheduler;
      var task = _task;
      if (scheduler != null)
        // ExecuteSynchronously: Enqueue runs inline on the completing thread
        // (inside SetResult for a TCS, or on the background thread for Task.Run)
        // so the continuation is always in the queue before the caller proceeds.
        task.ContinueWith(
          _ => scheduler.Enqueue(continuation),
          System.Threading.CancellationToken.None,
          TaskContinuationOptions.ExecuteSynchronously,
          TaskScheduler.Default);
      else
        task.GetAwaiter().OnCompleted(continuation);
    }

    public void UnsafeOnCompleted(Action continuation) => OnCompleted(continuation);
  }
}
