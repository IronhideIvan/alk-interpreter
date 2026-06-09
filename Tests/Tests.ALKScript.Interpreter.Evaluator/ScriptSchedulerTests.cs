// xUnit1031 warns against GetAwaiter().GetResult() in test methods and asks
// for async tests instead. These tests are intentionally synchronous because
// they manually install a custom SynchronizationContext and control it via
// Pump() — making the test async would route the test's own continuations
// through the installed context and interfere with what's under test.
#pragma warning disable xUnit1031

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ALKScript.Interpreter.Common.Evaluation.Scheduling;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Evaluator;
using ALKScript.Interpreter.Evaluator.Scheduling;

namespace Tests.ALKScript.Interpreter.Evaluator;

/// <summary>
/// Tests for <see cref="ScriptScheduler.Pump"/> — the per-tick API a host
/// calls to drive script resumption (docs/ASYNC_AWAIT_DESIGN.md decision #2).
///
/// Key semantics under test:
/// - <c>Pump()</c> takes a stable snapshot of the queue at the moment it is
///   called and runs exactly those callbacks — continuations posted *during*
///   a pump wait for the next call (decision #7: deterministic, stable ordering).
/// - When a script suspends on <c>await</c>, its continuation is later
///   <see cref="System.Threading.SynchronizationContext.Post"/>ed to the
///   scheduler; the next <c>Pump()</c> call resumes it.
/// </summary>
public class ScriptSchedulerTests : EvaluatorTestBase
{
  // ---------------------------------------------------------------------------
  // Pure scheduler behavior (no ALK script)
  // ---------------------------------------------------------------------------

  [Fact]
  public void Pump_WhenQueueIsEmpty_ReturnsZero()
  {
    var scheduler = new ScriptScheduler();

    int ran = scheduler.Pump();

    Assert.Equal(0, ran);
  }

  [Fact]
  public void Pump_WithQueuedCallbacks_RunsEachAndReturnsCount()
  {
    var scheduler = new ScriptScheduler();
    var log = new List<int>();

    scheduler.Post(_ => log.Add(1), null);
    scheduler.Post(_ => log.Add(2), null);
    scheduler.Post(_ => log.Add(3), null);

    int ran = scheduler.Pump();

    Assert.Equal(3, ran);
    Assert.Equal(new[] { 1, 2, 3 }, log);
  }

  [Fact]
  public void Pump_ContinuationQueuedDuringPump_IsNotRunUntilNextPump()
  {
    // Decision #7: Pump() snapshots the queue at call time. A callback that
    // posts another callback while running must not cause that inner callback
    // to execute in the same Pump() call — it waits for the next one.
    var scheduler = new ScriptScheduler();
    var log = new List<int>();

    scheduler.Post(_ =>
    {
      log.Add(1);
      scheduler.Post(_ => log.Add(3), null); // queued mid-pump
    }, null);
    scheduler.Post(_ => log.Add(2), null);

    int firstPump = scheduler.Pump();
    Assert.Equal(2, firstPump);
    Assert.Equal(new[] { 1, 2 }, log); // callback 3 not yet run

    int secondPump = scheduler.Pump();
    Assert.Equal(1, secondPump);
    Assert.Equal(new[] { 1, 2, 3 }, log);
  }

  [Fact]
  public void Pump_AfterAllCallbacksRun_ReturnsZeroOnSubsequentCall()
  {
    var scheduler = new ScriptScheduler();
    scheduler.Post(_ => { }, null);

    scheduler.Pump();
    int ran = scheduler.Pump();

    Assert.Equal(0, ran);
  }

  // ---------------------------------------------------------------------------
  // ALK script: async / await
  //
  // Note on evalTask: when the script calls "main()" at the top level without
  // "await", the top-level evaluation task completes immediately (the call
  // returns a TaskValue which the top-level ignores). main()'s *body* runs
  // asynchronously and may be suspended. We therefore use "recorded.Count"
  // as the proxy for "has the script resumed?" rather than "evalTask.IsCompleted".
  //
  // The exception is the single-await test, which uses a top-level "await"
  // directly (no "main()" wrapper) — there the evalTask itself suspends.
  // ---------------------------------------------------------------------------

  [Fact]
  public void Pump_ScriptSuspendedOnAwait_DoesNotResumeUntilPumpCalledAfterCompletion()
  {
    // A top-level "await fetch()" suspends the evaluator's own task.
    // The operation completes externally, and only the *next* Pump() call
    // resumes the script and calls record().
    var tcs = new TaskCompletionSource<ALKScriptValue>();
    var binder = new LambdaBinder(_ => tcs.Task);

    var recorded = new List<ALKScriptValue>();
    var bindings = new ScriptNativeBindings
    {
      ["record"] = args => { recorded.Add(args[0]); return NullValue.Instance; }
    };

    // Top-level "await" — the evalTask itself parks on tcs.Task.
    var graph = LoadGraph($"{RecordDeclaration}\nnative async function int fetch();\nrecord(await fetch());");
    var evaluator = new ProgramEvaluator(bindings, operationBinder: binder);
    var scheduler = new ScriptScheduler();

    Task evalTask = RunUnderScheduler(scheduler, () => evaluator.Evaluate(graph));

    Assert.False(evalTask.IsCompleted); // evalTask itself is suspended
    Assert.Empty(recorded);
    Assert.Equal(0, scheduler.Pump()); // nothing queued yet

    // Complete the operation — posts the script's resume continuation.
    tcs.SetResult(new IntValue(99));

    // One pump resumes the script and calls record().
    RunUnderScheduler(scheduler, () => { scheduler.Pump(); return Task.CompletedTask; });

    evalTask.GetAwaiter().GetResult();
    Assert.Equal(99L, Assert.IsType<IntValue>(Assert.Single(recorded)).Value);
  }

  [Fact]
  public void Pump_AsyncAwaitScript_MultipleAwaitsSuspendAndResumeInTurnAcrossPumps()
  {
    // Each "await" inside an async function suspends independently.
    // The host drives each resume by completing the pending operation and
    // calling Pump(). Between pumps the recorded list stays unchanged —
    // proving the script is truly parked and not racing the test thread.
    var tcs1 = new TaskCompletionSource<ALKScriptValue>();
    var tcs2 = new TaskCompletionSource<ALKScriptValue>();
    int callCount = 0;
    var binder = new LambdaBinder(_ => ++callCount == 1 ? tcs1.Task : tcs2.Task);

    var recorded = new List<ALKScriptValue>();
    var bindings = new ScriptNativeBindings
    {
      ["record"] = args => { recorded.Add(args[0]); return NullValue.Instance; }
    };

    const string source = """
      native function void record(Object v);
      native async function int fetch();
      async function void main() {
        record(await fetch());
        record(await fetch());
      }
      main();
      """;

    var scheduler = new ScriptScheduler();
    var evaluator = new ProgramEvaluator(
      new ScriptNativeBindings(bindings), operationBinder: binder);

    RunUnderScheduler(scheduler, () => evaluator.Evaluate(LoadGraph(source)));

    // main() is now suspended at its first "await fetch()".
    Assert.Empty(recorded);

    // Complete the first operation → resumes main() → records 10 → suspends at second await.
    tcs1.SetResult(new IntValue(10));
    RunUnderScheduler(scheduler, () => { scheduler.Pump(); return Task.CompletedTask; });

    Assert.Single(recorded);
    Assert.Equal(10L, Assert.IsType<IntValue>(recorded[0]).Value);

    // Complete the second operation → resumes main() → records 20 → main() finishes.
    tcs2.SetResult(new IntValue(20));
    RunUnderScheduler(scheduler, () => { scheduler.Pump(); return Task.CompletedTask; });

    Assert.Equal(2, recorded.Count);
    Assert.Equal(20L, Assert.IsType<IntValue>(recorded[1]).Value);
  }

  // ---------------------------------------------------------------------------
  // ALK script: await [] (whenAll)
  // ---------------------------------------------------------------------------

  [Fact]
  public void Pump_ScriptSuspendedOnWhenAll_ResumesAfterAllOperationsCompleteAndPumpIsCalled()
  {
    // "await [fetchA(), fetchB()]" suspends the script until both operations
    // complete. Completing both and calling Pump() once resumes the script
    // with an array of their results in element order.
    var tcsA = new TaskCompletionSource<ALKScriptValue>();
    var tcsB = new TaskCompletionSource<ALKScriptValue>();
    var binder = new LambdaBinder(op =>
      op.Name == "fetchA" ? tcsA.Task : tcsB.Task);

    var recorded = new List<ALKScriptValue>();
    var bindings = new ScriptNativeBindings
    {
      ["record"] = args => { recorded.Add(args[0]); return NullValue.Instance; }
    };

    const string source = """
      native function void record(Object v);
      native async function int fetchA();
      native async function int fetchB();
      async function void main() {
        var results = await [fetchA(), fetchB()];
        record(results[0]);
        record(results[1]);
      }
      main();
      """;

    var scheduler = new ScriptScheduler();
    RunUnderScheduler(scheduler,
      () => new ProgramEvaluator(bindings, operationBinder: binder).Evaluate(LoadGraph(source)));

    // main() is suspended at "await [fetchA(), fetchB()]".
    Assert.Empty(recorded);

    // Complete both operations — Task.WhenAll settles and posts the continuation.
    tcsA.SetResult(new IntValue(7));
    tcsB.SetResult(new IntValue(13));

    // A single Pump() is enough: whenAll settled synchronously above and
    // scheduled exactly one continuation (the script's resume point).
    RunUnderScheduler(scheduler, () => { scheduler.Pump(); return Task.CompletedTask; });

    Assert.Equal(2, recorded.Count);
    Assert.Equal(7L, Assert.IsType<IntValue>(recorded[0]).Value);
    Assert.Equal(13L, Assert.IsType<IntValue>(recorded[1]).Value);
  }

  [Fact]
  public void Pump_WhenAllScript_ScriptDoesNotResumeUntilBothOperationsComplete()
  {
    // Completing only one of the two whenAll members and pumping must not
    // resume the script — it only resumes once all members have settled.
    var tcsA = new TaskCompletionSource<ALKScriptValue>();
    var tcsB = new TaskCompletionSource<ALKScriptValue>();
    var binder = new LambdaBinder(op =>
      op.Name == "fetchA" ? tcsA.Task : tcsB.Task);

    var recorded = new List<ALKScriptValue>();
    var bindings = new ScriptNativeBindings
    {
      ["record"] = args => { recorded.Add(args[0]); return NullValue.Instance; }
    };

    const string source = """
      native function void record(Object v);
      native async function int fetchA();
      native async function int fetchB();
      async function void main() {
        var results = await [fetchA(), fetchB()];
        record(results[0]);
        record(results[1]);
      }
      main();
      """;

    var scheduler = new ScriptScheduler();
    RunUnderScheduler(scheduler,
      () => new ProgramEvaluator(bindings, operationBinder: binder).Evaluate(LoadGraph(source)));

    // Only complete fetchA — whenAll is still waiting for fetchB.
    tcsA.SetResult(new IntValue(5));
    RunUnderScheduler(scheduler, () => { scheduler.Pump(); return Task.CompletedTask; });

    Assert.Empty(recorded); // script has not resumed yet

    // Complete fetchB — whenAll settles, one pump resumes the script.
    tcsB.SetResult(new IntValue(6));
    RunUnderScheduler(scheduler, () => { scheduler.Pump(); return Task.CompletedTask; });

    Assert.Equal(2, recorded.Count);
    Assert.Equal(5L, Assert.IsType<IntValue>(recorded[0]).Value);
    Assert.Equal(6L, Assert.IsType<IntValue>(recorded[1]).Value);
  }

  // ---------------------------------------------------------------------------
  // Test infrastructure
  // ---------------------------------------------------------------------------

  /// <summary>
  /// Installs <paramref name="scheduler"/> as <see cref="SynchronizationContext.Current"/>,
  /// invokes <paramref name="action"/>, then restores the previous context.
  /// The scheduler must be current whenever a script's <c>await</c> suspension
  /// point is hit, so that the continuation is captured on this scheduler
  /// rather than the default thread-pool context.
  /// </summary>
  private static Task RunUnderScheduler(ScriptScheduler scheduler, Func<Task> action)
  {
    var previous = SynchronizationContext.Current;
    SynchronizationContext.SetSynchronizationContext(scheduler);
    try
    {
      return action();
    }
    finally
    {
      SynchronizationContext.SetSynchronizationContext(previous);
    }
  }

  private sealed class LambdaBinder : IAsyncOperationBinder
  {
    private readonly Func<PendingOperation, Task<ALKScriptValue>> _start;

    internal LambdaBinder(Func<PendingOperation, Task<ALKScriptValue>> start) => _start = start;

    public Task<ALKScriptValue> Start(PendingOperation operation) => _start(operation);
    public void Discard(PendingOperation operation, Action<Exception> onFault) => _ = _start(operation);
    public void OnOperationFaulted(PendingOperation operation, Exception fault) { }
  }
}
