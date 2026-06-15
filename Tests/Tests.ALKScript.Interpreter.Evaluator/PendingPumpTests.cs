using System;
using System.Collections.Generic;
using ALKScript.Interpreter.Common.Evaluation.Scheduling;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Evaluator.Cursor;

namespace Tests.ALKScript.Interpreter.Evaluator;

/// <summary>
/// Covers the Task-free suspension model: a binder whose
/// <see cref="IAsyncOperationBinder.Start"/> reports <see cref="OperationStatus.Pending"/>
/// suspends the run via <see cref="ProgramRunResult.Awaiting"/>, and
/// <see cref="ProgramRun.Pump"/> repeatedly polls via
/// <see cref="IAsyncOperationBinder.Poll"/> until the operation settles and the
/// run resumes — for both a single <c>await</c> and a composite
/// <c>await [a, b]</c> with a mix of immediately-resolved and initially-pending
/// elements.
/// </summary>
public class PendingPumpTests : EvaluatorTestBase
{
  [Fact]
  public void SingleAwait_StartReturnsPending_PumpResumesOncePollResolves()
  {
    var binder = new PollBinder(op => null);

    var graph = LoadGraph(
      $"{RecordDeclaration}\nnative function thunk<int> fetch();\nfunction void main() {{\n  var fetched = await fetch();\n  record(fetched);\n}}\nmain();");

    var recorded = new List<ALKScriptValue>();
    var bindings = new ScriptNativeBindings
    {
      ["record"] = arguments => { recorded.Add(arguments[0]); return NullValue.Instance; }
    };

    var run = ProgramRun.Start(new CursorProgramEvaluator(bindings, null, binder), graph);

    Assert.Equal(ProgramRunResult.Awaiting, run.Result);
    Assert.Empty(recorded);

    // Not yet ready — Pump leaves the run suspended.
    Assert.Equal(ProgramRunResult.Awaiting, run.Pump());
    Assert.Empty(recorded);

    // Now the host's operation has settled — Pump resumes the run.
    binder.Settle("fetch", new IntValue(42));
    Assert.Equal(ProgramRunResult.Completed, run.Pump());

    var value = Assert.IsType<IntValue>(Assert.Single(recorded));
    Assert.Equal(42L, value.Value);
  }

  [Fact]
  public void CompositeAwait_OneElementPendingOneResolved_PumpResumesOncePollResolvesTheOther()
  {
    var binder = new PollBinder(op => op.Name == "a" ? new IntValue(1) : null);

    var graph = LoadGraph(
      $"{RecordDeclaration}\nnative function thunk<int> a();\nnative function thunk<int> b();\nfunction void main() {{\n  var results = await [a(), b()];\n  record(results[0]);\n  record(results[1]);\n}}\nmain();");

    var recorded = new List<ALKScriptValue>();
    var bindings = new ScriptNativeBindings
    {
      ["record"] = arguments => { recorded.Add(arguments[0]); return NullValue.Instance; }
    };

    var run = ProgramRun.Start(new CursorProgramEvaluator(bindings, null, binder), graph);

    Assert.Equal(ProgramRunResult.Awaiting, run.Result);
    Assert.NotNull(run.PendingAwait!.CompositeElements);
    Assert.Empty(recorded);

    // "b" hasn't settled yet — Pump leaves the run suspended.
    Assert.Equal(ProgramRunResult.Awaiting, run.Pump());
    Assert.Empty(recorded);

    // Now "b" settles too — Pump resumes the run with both results.
    binder.Settle("b", new IntValue(2));
    Assert.Equal(ProgramRunResult.Completed, run.Pump());

    Assert.Equal(2, recorded.Count);
    Assert.Equal(1L, Assert.IsType<IntValue>(recorded[0]).Value);
    Assert.Equal(2L, Assert.IsType<IntValue>(recorded[1]).Value);
  }

  /// <summary>
  /// A binder where <see cref="Start"/> immediately reports the operation's
  /// settled value if <paramref name="initial"/> already has one for it (via
  /// <paramref name="seed"/>), otherwise <see cref="OperationStatus.Pending"/>;
  /// <see cref="Poll"/> re-checks the same map, which <see cref="Settle"/>
  /// mutates from the test.
  /// </summary>
  private sealed class PollBinder : IAsyncOperationBinder
  {
    private readonly Func<PendingOperation, ALKScriptValue?> _seed;
    private readonly Dictionary<string, ALKScriptValue> _settled = new();

    internal PollBinder(Func<PendingOperation, ALKScriptValue?> seed) => _seed = seed;

    internal void Settle(string operationName, ALKScriptValue value) => _settled[operationName] = value;

    public OperationStatus Start(PendingOperation operation)
    {
      var seeded = _seed(operation);
      if (seeded != null)
      {
        _settled[operation.Name] = seeded;
        return new OperationStatus.Resolved(seeded);
      }

      return OperationStatus.Pending.Instance;
    }

    public OperationStatus Poll(PendingOperation operation) =>
      _settled.TryGetValue(operation.Name, out var value)
        ? new OperationStatus.Resolved(value)
        : OperationStatus.Pending.Instance;

    public void Discard(PendingOperation operation, Action<Exception> onFault) { }

    public void OnOperationFaulted(PendingOperation operation, Exception fault) { }
  }
}
