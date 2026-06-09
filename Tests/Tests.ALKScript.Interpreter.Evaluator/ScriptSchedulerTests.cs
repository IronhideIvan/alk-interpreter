using System;
using System.Collections.Generic;
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
///   called and runs exactly those callbacks — continuations posted <em>during</em>
///   a pump wait for the next call (decision #7: deterministic, stable ordering).
/// - When a script suspends on <c>await</c>, its continuation is enqueued via
///   <see cref="IScriptScheduler.Enqueue"/>; the next <c>Pump()</c> resumes it.
/// - No <see cref="System.Threading.SynchronizationContext"/> installation is
///   needed — the scheduler is passed directly to <see cref="ProgramEvaluator"/>
///   and the evaluator's custom-awaitable wrappers route continuations to it.
/// </summary>
public class ScriptSchedulerTests : EvaluatorTestBase
{
  // ---------------------------------------------------------------------------
  // Pure scheduler behaviour (no ALK script)
  // ---------------------------------------------------------------------------

  [Fact]
  public void Pump_WhenQueueIsEmpty_ReturnsZero()
  {
    var scheduler = new ScriptScheduler();

    Assert.Equal(0, scheduler.Pump());
  }

  [Fact]
  public void Pump_WithQueuedCallbacks_RunsEachAndReturnsCount()
  {
    var scheduler = new ScriptScheduler();
    var log = new List<int>();

    scheduler.Enqueue(() => log.Add(1));
    scheduler.Enqueue(() => log.Add(2));
    scheduler.Enqueue(() => log.Add(3));

    int ran = scheduler.Pump();

    Assert.Equal(3, ran);
    Assert.Equal(new[] { 1, 2, 3 }, log);
  }

  [Fact]
  public void Pump_ContinuationEnqueuedDuringPump_IsNotRunUntilNextPump()
  {
    // Decision #7: Pump() snapshots the queue at call time. A callback that
    // enqueues another callback while running must not cause that inner
    // callback to execute in the same Pump() call — it waits for the next one.
    var scheduler = new ScriptScheduler();
    var log = new List<int>();

    scheduler.Enqueue(() =>
    {
      log.Add(1);
      scheduler.Enqueue(() => log.Add(3)); // enqueued mid-pump
    });
    scheduler.Enqueue(() => log.Add(2));

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
    scheduler.Enqueue(() => { });

    scheduler.Pump();

    Assert.Equal(0, scheduler.Pump());
  }

  // ---------------------------------------------------------------------------
  // ALK script: async / await
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

    // Top-level "await" — the Evaluate task parks on tcs.Task and returns an
    // incomplete Task. We discard it here; driving it is the scheduler's job.
    var scheduler = new ScriptScheduler();
    var graph = LoadGraph($"{RecordDeclaration}\nnative async function int fetch();\nrecord(await fetch());");
    var evaluator = new ProgramEvaluator(bindings, operationBinder: binder, scheduler: scheduler);
    evaluator.Evaluate(graph); // starts evaluation; suspends at "await fetch()"

    Assert.Empty(recorded);
    Assert.Equal(0, scheduler.Pump()); // nothing enqueued yet

    // Complete the operation — the continuation is enqueued on the scheduler.
    tcs.SetResult(new IntValue(99));

    int ran = scheduler.Pump(); // resumes the script; calls record()
    Assert.True(ran >= 1);

    Assert.Equal(99L, Assert.IsType<IntValue>(Assert.Single(recorded)).Value);
  }

  [Fact]
  public void Pump_AsyncAwaitScript_MultipleAwaitsSuspendAndResumeInTurnAcrossPumps()
  {
    // Each "await" inside an async function suspends independently.
    // Completing the pending operation and calling Pump() drives each resume.
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
    var evaluator = new ProgramEvaluator(bindings, operationBinder: binder, scheduler: scheduler);
    evaluator.Evaluate(LoadGraph(source)); // runs until main() suspends at first await

    // main() is suspended at its first "await fetch()".
    Assert.Empty(recorded);

    // Complete the first operation → resumes main() → records 10 → suspends at second await.
    tcs1.SetResult(new IntValue(10));
    scheduler.Pump();

    Assert.Single(recorded);
    Assert.Equal(10L, Assert.IsType<IntValue>(recorded[0]).Value);

    // Complete the second operation → resumes main() → records 20 → main() finishes.
    tcs2.SetResult(new IntValue(20));
    scheduler.Pump();

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
    var binder = new LambdaBinder(op => op.Name == "fetchA" ? tcsA.Task : tcsB.Task);

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
    var evaluator = new ProgramEvaluator(bindings, operationBinder: binder, scheduler: scheduler);
    evaluator.Evaluate(LoadGraph(source)); // suspends at "await [fetchA(), fetchB()]"

    Assert.Empty(recorded);

    // Complete both operations — Task.WhenAll settles and enqueues the continuation.
    tcsA.SetResult(new IntValue(7));
    tcsB.SetResult(new IntValue(13));

    // A single Pump() is enough: Task.WhenAll settled synchronously above.
    scheduler.Pump();

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
    var binder = new LambdaBinder(op => op.Name == "fetchA" ? tcsA.Task : tcsB.Task);

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
    var evaluator = new ProgramEvaluator(bindings, operationBinder: binder, scheduler: scheduler);
    evaluator.Evaluate(LoadGraph(source));

    // Only complete fetchA — whenAll is still waiting for fetchB.
    tcsA.SetResult(new IntValue(5));
    scheduler.Pump();

    Assert.Empty(recorded); // script has not resumed yet

    // Complete fetchB — whenAll settles, one pump resumes the script.
    tcsB.SetResult(new IntValue(6));
    scheduler.Pump();

    Assert.Equal(2, recorded.Count);
    Assert.Equal(5L, Assert.IsType<IntValue>(recorded[0]).Value);
    Assert.Equal(6L, Assert.IsType<IntValue>(recorded[1]).Value);
  }

  // ---------------------------------------------------------------------------
  // RunUntilComplete
  // ---------------------------------------------------------------------------

  [Fact]
  public void RunUntilComplete_SimpleScript_RunsToCompletion()
  {
    // A script with no async operations completes in one synchronous pass.
    var recorded = new List<ALKScriptValue>();
    var bindings = new ScriptNativeBindings
    {
      ["record"] = args => { recorded.Add(args[0]); return NullValue.Instance; }
    };

    var scheduler = new ScriptScheduler();
    var evaluator = new ProgramEvaluator(bindings, scheduler: scheduler);
    scheduler.RunUntilComplete(evaluator.Evaluate(LoadGraph($"{RecordDeclaration}record(42);")));

    Assert.Equal(42L, Assert.IsType<IntValue>(Assert.Single(recorded)).Value);
  }

  [Fact]
  public void RunUntilComplete_AsyncScript_DrivesSchedulerUntilScriptFinishes()
  {
    // RunUntilComplete pumps continuations internally — the caller does not
    // need to drive Pump() manually.
    var tcs = new TaskCompletionSource<ALKScriptValue>();
    var binder = new LambdaBinder(_ => tcs.Task);

    var recorded = new List<ALKScriptValue>();
    var bindings = new ScriptNativeBindings
    {
      ["record"] = args => { recorded.Add(args[0]); return NullValue.Instance; }
    };

    const string source = """
      native function void record(Object v);
      native async function int fetch();
      record(await fetch());
      """;

    var scheduler = new ScriptScheduler();
    var evaluator = new ProgramEvaluator(bindings, operationBinder: binder, scheduler: scheduler);
    var evaluation = evaluator.Evaluate(LoadGraph(source));

    // Complete the operation on a background thread while RunUntilComplete is
    // blocking — it must wake up, pump the continuation, and then return.
#pragma warning disable xUnit1051 // CancellationToken not needed: fire-and-forget background delay
    Task.Run(async () =>
    {
      await Task.Delay(10);
      tcs.SetResult(new IntValue(7));
    });
#pragma warning restore xUnit1051

    scheduler.RunUntilComplete(evaluation);

    Assert.Equal(7L, Assert.IsType<IntValue>(Assert.Single(recorded)).Value);
  }

  [Fact]
  public void RunUntilComplete_FireAndForgetAsync_DrainsRemainingContinuations()
  {
    // A script that calls an async function without awaiting it (fire-and-
    // forget) causes the top-level Evaluate task to complete while the async
    // function's continuations are still pending. RunUntilComplete must drain
    // those continuations before returning.
    var tcs = new TaskCompletionSource<ALKScriptValue>();
    var binder = new LambdaBinder(_ => tcs.Task);

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
      }
      main();
      """;

    var scheduler = new ScriptScheduler();
    var evaluator = new ProgramEvaluator(bindings, operationBinder: binder, scheduler: scheduler);
    var evaluation = evaluator.Evaluate(LoadGraph(source));

    // The top-level Evaluate task is already complete (main() was fire-and-
    // forget), but main() itself is suspended on tcs.Task. Completing the TCS
    // now — before calling RunUntilComplete — enqueues main()'s continuation
    // synchronously (ExecuteSynchronously guarantee). RunUntilComplete will
    // find it in the queue and drain it even though the top-level task is done.
    tcs.SetResult(new IntValue(99));

    scheduler.RunUntilComplete(evaluation);

    Assert.Equal(99L, Assert.IsType<IntValue>(Assert.Single(recorded)).Value);
  }

  [Fact]
  public void RunUntilComplete_FaultedScript_RethrowsException()
  {
    // An uncaught exception in script code should propagate out of
    // RunUntilComplete, unwrapped from its Task container, so the host
    // receives it as a plain exception rather than an AggregateException.
    var bindings = new ScriptNativeBindings
    {
      ["boom"] = _ => throw new InvalidOperationException("script faulted")
    };

    const string source = """
      native function void boom();
      boom();
      """;

    var scheduler = new ScriptScheduler();
    var evaluator = new ProgramEvaluator(bindings, scheduler: scheduler);

    var ex = Assert.Throws<InvalidOperationException>(
      () => scheduler.RunUntilComplete(evaluator.Evaluate(LoadGraph(source))));

    Assert.Equal("script faulted", ex.Message);
  }

  // ---------------------------------------------------------------------------
  // Test infrastructure
  // ---------------------------------------------------------------------------

  private sealed class LambdaBinder : IAsyncOperationBinder
  {
    private readonly Func<PendingOperation, Task<ALKScriptValue>> _start;

    internal LambdaBinder(Func<PendingOperation, Task<ALKScriptValue>> start) => _start = start;

    public Task<ALKScriptValue> Start(PendingOperation operation) => _start(operation);
    public void Discard(PendingOperation operation, Action<Exception> onFault) => _ = _start(operation);
    public void OnOperationFaulted(PendingOperation operation, Exception fault) { }
  }
}
