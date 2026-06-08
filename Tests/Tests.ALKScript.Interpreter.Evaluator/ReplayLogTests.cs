using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ALKScript.Interpreter.Common.Evaluation.Scheduling;
using ALKScript.Interpreter.Common.Evaluation.Values;

namespace Tests.ALKScript.Interpreter.Evaluator;

/// <summary>
/// Covers the record-and-replay save/load mechanism (docs/ASYNC_AWAIT_DESIGN.md
/// decision #17): the evaluator appends an <see cref="OperationLogEntry"/> for
/// each <c>async native</c> operation it runs live, and when seeded with a
/// prior log it replays those entries positionally — returning recorded results
/// immediately without calling the binder — until the log is exhausted, then
/// continues live.
/// </summary>
public class ReplayLogTests : EvaluatorTestBase
{
  // -------------------------------------------------------------------------
  // Recording
  // -------------------------------------------------------------------------

  [Fact]
  public void Evaluate_AwaitOnAsyncNative_AppendsResultEntryToLog()
  {
    var binder = new TrackingBinder(_ => Task.FromResult((ALKScriptValue)new IntValue(42)));

    var (_, log) = RunAndCaptureLog(
      $"{RecordDeclaration}\nnative async function int fetch();\nasync function void main() {{\n  record(await fetch());\n}}\nmain();",
      binder);

    var entry = Assert.Single(log);
    Assert.Equal("fetch", entry.Operation.Name);
    Assert.False(entry.IsFaulted);
    Assert.Equal(42L, Assert.IsType<IntValue>(entry.Result).Value);
  }

  [Fact]
  public void Evaluate_AwaitOnFaultingAsyncNative_AppendsFaultEntryToLog()
  {
    var binder = new TrackingBinder(_ => Task.FromException<ALKScriptValue>(new InvalidOperationException("boom")));

    var (_, log) = RunAndCaptureLog(
      $"{RecordDeclaration}\nnative async function int fetch();\nasync function void main() {{\n  try {{\n    await fetch();\n  }} catch (string e) {{\n    record(e);\n  }}\n}}\nmain();",
      binder);

    var entry = Assert.Single(log);
    Assert.Equal("fetch", entry.Operation.Name);
    Assert.True(entry.IsFaulted);
    Assert.Equal("boom", entry.FaultMessage);
  }

  [Fact]
  public void Evaluate_MultipleAwaits_AppendsOneEntryPerAwait()
  {
    int call = 0;
    var binder = new TrackingBinder(_ => Task.FromResult((ALKScriptValue)new IntValue(++call)));

    var (_, log) = RunAndCaptureLog(
      $"{RecordDeclaration}\nnative async function int fetch();\nasync function void main() {{\n  record(await fetch());\n  record(await fetch());\n}}\nmain();",
      binder);

    Assert.Equal(2, log.Count);
    Assert.Equal(1L, Assert.IsType<IntValue>(log[0].Result).Value);
    Assert.Equal(2L, Assert.IsType<IntValue>(log[1].Result).Value);
  }

  // -------------------------------------------------------------------------
  // Replay — full log
  // -------------------------------------------------------------------------

  [Fact]
  public void Evaluate_WithFullReplayLog_ReturnsRecordedResultsWithoutCallingBinder()
  {
    // Capture a log from a live run, then replay with a binder that would
    // return a different value — verify the recorded value wins.
    var liveBinder = new TrackingBinder(_ => Task.FromResult((ALKScriptValue)new IntValue(42)));

    var (_, log) = RunAndCaptureLog(
      $"{RecordDeclaration}\nnative async function int fetch();\nasync function void main() {{\n  record(await fetch());\n}}\nmain();",
      liveBinder);

    // Replay binder would return 99, but replay should ignore it.
    var replayBinder = new TrackingBinder(_ => Task.FromResult((ALKScriptValue)new IntValue(99)));

    var recorded = RunWithReplayLog(
      $"{RecordDeclaration}\nnative async function int fetch();\nasync function void main() {{\n  record(await fetch());\n}}\nmain();",
      replayBinder, log);

    // Result must come from log, not binder.
    var value = Assert.IsType<IntValue>(Assert.Single(recorded));
    Assert.Equal(42L, value.Value);

    // Binder must not have been called during replay.
    Assert.Equal(0, replayBinder.StartCallCount);
  }

  [Fact]
  public void Evaluate_WithFaultLogEntry_ReplaysSurfacesThrownSignalWithoutCallingBinder()
  {
    var faultLog = new List<OperationLogEntry>
    {
      OperationLogEntry.FromFault(new PendingOperation("fetch", Array.Empty<ALKScriptValue>()), "replayed-boom")
    };

    var replayBinder = new TrackingBinder(_ => Task.FromResult((ALKScriptValue)NullValue.Instance));

    var recorded = RunWithReplayLog(
      $"{RecordDeclaration}\nnative async function int fetch();\nasync function void main() {{\n  try {{\n    await fetch();\n  }} catch (string e) {{\n    record(e);\n  }}\n}}\nmain();",
      replayBinder, faultLog);

    var fault = Assert.IsType<StringValue>(Assert.Single(recorded));
    Assert.Equal("replayed-boom", fault.Value);
    Assert.Equal(0, replayBinder.StartCallCount);
  }

  // -------------------------------------------------------------------------
  // Replay — partial log (mid-script save/load)
  // -------------------------------------------------------------------------

  [Fact]
  public void Evaluate_WithPartialLog_ReplaysFirstAwaitAndRunsSecondLive()
  {
    // The saved log only covers the first await. The second await runs live
    // and appends a new entry — verifying the handoff from replay to live.
    int liveCall = 0;
    var liveBinder = new TrackingBinder(_ => Task.FromResult((ALKScriptValue)new IntValue(++liveCall)));

    var (_, fullLog) = RunAndCaptureLog(
      $"{RecordDeclaration}\nnative async function int fetch();\nasync function void main() {{\n  record(await fetch());\n  record(await fetch());\n}}\nmain();",
      liveBinder);

    Assert.Equal(2, fullLog.Count); // sanity

    // Seed only the first log entry.
    var partialLog = new List<OperationLogEntry> { fullLog[0] };

    int replayLiveCall = 0;
    var replayBinder = new TrackingBinder(_ => Task.FromResult((ALKScriptValue)new IntValue(200 + ++replayLiveCall)));

    var recorded = RunWithReplayLog(
      $"{RecordDeclaration}\nnative async function int fetch();\nasync function void main() {{\n  record(await fetch());\n  record(await fetch());\n}}\nmain();",
      replayBinder, partialLog);

    Assert.Equal(2, recorded.Count);
    // First: from replay log (original value).
    Assert.Equal(Assert.IsType<IntValue>(fullLog[0].Result).Value, Assert.IsType<IntValue>(recorded[0]).Value);
    // Second: from live binder (201).
    Assert.Equal(201L, Assert.IsType<IntValue>(recorded[1]).Value);
    // Binder called exactly once (for the second await only).
    Assert.Equal(1, replayBinder.StartCallCount);
  }

  // -------------------------------------------------------------------------
  // Replay — whenAll
  // -------------------------------------------------------------------------

  [Fact]
  public void Evaluate_WhenAll_RecordsOneEntryPerElement()
  {
    int call = 0;
    var binder = new TrackingBinder(_ => Task.FromResult((ALKScriptValue)new IntValue(++call)));

    var (_, log) = RunAndCaptureLog(
      $"{RecordDeclaration}\nnative async function int a();\nnative async function int b();\nasync function void main() {{\n  var results = await [a(), b()];\n  record(results[0]);\n  record(results[1]);\n}}\nmain();",
      binder);

    Assert.Equal(2, log.Count);
    Assert.Equal("a", log[0].Operation.Name);
    Assert.Equal("b", log[1].Operation.Name);
  }

  [Fact]
  public void Evaluate_WhenAll_WithFullLog_ReplaysWithoutCallingBinder()
  {
    int call = 0;
    var liveBinder = new TrackingBinder(_ => Task.FromResult((ALKScriptValue)new IntValue(++call * 10)));

    var (_, log) = RunAndCaptureLog(
      $"{RecordDeclaration}\nnative async function int a();\nnative async function int b();\nasync function void main() {{\n  var results = await [a(), b()];\n  record(results[0]);\n  record(results[1]);\n}}\nmain();",
      liveBinder);

    var replayBinder = new TrackingBinder(_ => Task.FromResult((ALKScriptValue)new IntValue(999)));

    var recorded = RunWithReplayLog(
      $"{RecordDeclaration}\nnative async function int a();\nnative async function int b();\nasync function void main() {{\n  var results = await [a(), b()];\n  record(results[0]);\n  record(results[1]);\n}}\nmain();",
      replayBinder, log);

    Assert.Equal(2, recorded.Count);
    Assert.Equal(10L, Assert.IsType<IntValue>(recorded[0]).Value);
    Assert.Equal(20L, Assert.IsType<IntValue>(recorded[1]).Value);
    Assert.Equal(0, replayBinder.StartCallCount);
  }

  // -------------------------------------------------------------------------
  // Discard does not re-fire replayed operations
  // -------------------------------------------------------------------------

  [Fact]
  public void Evaluate_ReplayedOperation_IsNotDiscardedAtEndOfScript()
  {
    // A replayed operation must be marked consumed so ProgramEvaluator's
    // end-of-script Discard sweep doesn't fire it off a second time.
    var replayBinder = new TrackingBinder(_ => Task.FromResult((ALKScriptValue)NullValue.Instance));

    var log = new List<OperationLogEntry>
    {
      OperationLogEntry.FromResult(new PendingOperation("move", Array.Empty<ALKScriptValue>()), NullValue.Instance)
    };

    // "move()" is awaited — the replay log covers it.
    RunWithReplayLog("native async function void move(); await move();", replayBinder, log);

    Assert.Equal(0, replayBinder.StartCallCount);
    Assert.Empty(replayBinder.Discarded);
  }

  // -------------------------------------------------------------------------
  // Test infrastructure
  // -------------------------------------------------------------------------

  private sealed class TrackingBinder : IAsyncOperationBinder
  {
    private readonly Func<PendingOperation, Task<ALKScriptValue>> _start;

    internal int StartCallCount { get; private set; }
    internal readonly List<PendingOperation> Discarded = new List<PendingOperation>();

    internal TrackingBinder(Func<PendingOperation, Task<ALKScriptValue>> start) => _start = start;

    public Task<ALKScriptValue> Start(PendingOperation operation)
    {
      StartCallCount++;
      return _start(operation);
    }

    public void Discard(PendingOperation operation, Action<Exception> onFault)
    {
      Discarded.Add(operation);
      _ = _start(operation);
    }

    public void OnOperationFaulted(PendingOperation operation, Exception fault) { }
  }
}
