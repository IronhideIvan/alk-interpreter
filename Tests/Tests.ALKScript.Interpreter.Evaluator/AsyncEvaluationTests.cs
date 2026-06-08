using System.Collections.Generic;
using System.Threading.Tasks;
using ALKScript.Interpreter.Common.Evaluation.Scheduling;
using ALKScript.Interpreter.Common.Evaluation.Values;

namespace Tests.ALKScript.Interpreter.Evaluator;

/// <summary>
/// End-to-end coverage for "real" <c>async</c>/<c>await</c> suspension (see
/// docs/ASYNC_AWAIT_DESIGN.md): an <c>await</c> on a <see cref="TaskValue"/>
/// genuinely parks evaluation on the underlying <see cref="System.Threading.Tasks.Task"/>
/// — including across a real cross-thread completion — and resumes with
/// either the produced value or a catchable fault, and an <c>async</c>
/// function call returns a <see cref="TaskValue"/> immediately rather than
/// blocking until its body completes.
/// </summary>
public class AsyncEvaluationTests : EvaluatorTestBase
{
  [Fact]
  public void Evaluate_AwaitOnNativeReturningAlreadyCompletedTask_ResolvesToItsValue()
  {
    var recorded = Run(
      $"{RecordDeclaration}\nnative async function int fetch();\nasync function void main() {{\n  record(await fetch());\n}}\nmain();",
      new FuncBinder(_ => Task.FromResult((ALKScriptValue)new IntValue(42))));

    var value = Assert.IsType<IntValue>(Assert.Single(recorded));
    Assert.Equal(42L, value.Value);
  }

  [Fact]
  public void Evaluate_AwaitOnPendingTaskLaterCompletedSynchronously_SuspendsAndResumesWithItsValue()
  {
    // "fetch" hands back a *pending* task — "await" must genuinely suspend
    // mid-script on it (there's nothing to resolve to yet). A later
    // statement, "resolve()", settles it: by default a TaskCompletionSource's
    // continuations run synchronously on the thread that completes it, so
    // this resumes "main"'s suspended body — and its "record" call — right
    // there, deterministically, with no scheduler required. (A real host
    // would instead settle this from off-thread, pumped by the scheduler a
    // later phase introduces — but the suspend/resume mechanics under test
    // here are exactly the same either way.)
    var pending = new TaskCompletionSource<ALKScriptValue>();

    var recorded = Run(
      $"{RecordDeclaration}\nnative async function int fetch();\nnative function void resolve();\nasync function void main() {{\n  record(await fetch());\n}}\nmain();\nresolve();",
      new FuncBinder(_ => pending.Task),
      new ScriptNativeBindings
      {
        ["resolve"] = _ => { pending.SetResult(new IntValue(7)); return NullValue.Instance; }
      });

    var value = Assert.IsType<IntValue>(Assert.Single(recorded));
    Assert.Equal(7L, value.Value);
  }

  [Fact]
  public void Evaluate_AwaitOnPendingTaskLaterFaultedSynchronously_RaisesACatchableThrownSignal()
  {
    var pending = new TaskCompletionSource<ALKScriptValue>();

    var recorded = Run(
      $"{RecordDeclaration}\nnative async function int fetch();\nnative function void reject();\nasync function void main() {{\n  try {{\n    await fetch();\n  }} catch (string e) {{\n    record(e);\n  }}\n}}\nmain();\nreject();",
      new FuncBinder(_ => pending.Task),
      new ScriptNativeBindings
      {
        ["reject"] = _ => { pending.SetException(new System.InvalidOperationException("boom")); return NullValue.Instance; }
      });

    var value = Assert.IsType<StringValue>(Assert.Single(recorded));
    Assert.Equal("boom", value.Value);
  }

  [Fact]
  public void Evaluate_AwaitOnPlainValue_YieldsItDirectly()
  {
    var recorded = Run($"{RecordDeclaration}\nasync function void main() {{\n  record(await 1);\n}}\nmain();");

    var value = Assert.IsType<IntValue>(Assert.Single(recorded));
    Assert.Equal(1L, value.Value);
  }

  [Fact]
  public void Evaluate_CallingAnAsyncFunction_ReturnsATaskValueRatherThanBlockingUntilItCompletes()
  {
    // Calling an "async" function must hand back an awaitable immediately —
    // not block until the body has fully run — so the caller can do other
    // work alongside it (or "await" it later). That's the entire point of
    // "let t = asyncFn(); ...; await t;" being meaningfully different from
    // "await asyncFn();".
    var recorded = Run($"{RecordDeclaration}\nasync function int compute() {{\n  return 21 * 2;\n}}\nasync function void main() {{\n  var t = compute();\n  record(t);\n  record(await t);\n}}\nmain();");

    Assert.Equal(2, recorded.Count);
    Assert.IsType<TaskValue>(recorded[0]);
    var resolved = Assert.IsType<IntValue>(recorded[1]);
    Assert.Equal(42L, resolved.Value);
  }

  [Fact]
  public void Evaluate_AwaitOnTaskCompletedFromABackgroundThread_SuspendsAndSafelyResumesOnTheScriptThread()
  {
    // This is the scenario Phase 3 had to work around (see its "known
    // limitation" tests' comments): a native hands back a task that genuinely
    // settles on a background thread — via Task.Run — fully concurrently with
    // the script. Without a scheduler, that continuation would either run
    // unobserved (the driver had already returned) or race the script on
    // whatever thread it happened to land on. Now that EvaluatorTestBase
    // drives evaluation through ScriptScheduler.RunUntilComplete, the
    // continuation is Post()ed back to — and only ever runs on — the single
    // host thread that's pumping the script, exactly like every other
    // continuation: genuinely concurrent settlement, but cooperative,
    // single-threaded resumption.
    //
    // NB: this awaits directly at the top level rather than from inside an
    // "async function main()" — top-level statements run as part of the
    // overall (also Task-returning) program evaluation, so awaiting here
    // suspends *that* task, and RunUntilComplete genuinely waits for it.
    // (Awaiting only from within a fire-and-forget "async" call wouldn't:
    // per the eager-start model such a call returns immediately, so the
    // top-level "Evaluate" task — and thus RunUntilComplete — would
    // consider the program already finished. Observing a fire-and-forget
    // async call's *eventual* completion is exactly the "Discard"/lazy-start
    // mechanism the design defers past this phase.)
    var recorded = Run(
      $"{RecordDeclaration}\nnative async function int fetch();\nrecord(await fetch());",
      new FuncBinder(_ => Task.Run(async () =>
      {
        await Task.Delay(20);
        return (ALKScriptValue)new IntValue(99);
      })));

    var value = Assert.IsType<IntValue>(Assert.Single(recorded));
    Assert.Equal(99L, value.Value);
  }

  private static IReadOnlyList<ALKScriptValue> Run(string source, IAsyncOperationBinder operationBinder, ScriptNativeBindings? extraBindings = null)
  {
    var recorded = new List<ALKScriptValue>();
    var bindings = new ScriptNativeBindings(extraBindings ?? new ScriptNativeBindings())
    {
      ["record"] = arguments =>
      {
        recorded.Add(arguments[0]);
        return NullValue.Instance;
      }
    };

    RunWithOperationBinder(source, bindings, operationBinder);

    return recorded;
  }

  private sealed class FuncBinder : IAsyncOperationBinder
  {
    private readonly Func<PendingOperation, Task<ALKScriptValue>> _start;
    internal FuncBinder(Func<PendingOperation, Task<ALKScriptValue>> start) => _start = start;
    public Task<ALKScriptValue> Start(PendingOperation operation) => _start(operation);
  }

  [Fact]
  public void Evaluate_AsyncFunctionThatAwaitsASuspendingOperation_PropagatesSuspensionToItsCaller()
  {
    // "load"'s own body suspends mid-call (its "await fetch()" parks on a
    // still-pending task) — proving suspension composes through user-defined
    // async functions, not just directly-awaited natives: "main"'s
    // "await load()" is transitively parked until "load"'s body — and
    // therefore "fetch"'s task — settles.
    var pending = new TaskCompletionSource<ALKScriptValue>();

    var recorded = Run(
      $"{RecordDeclaration}\nnative async function int fetch();\nnative function void resolve();\nasync function int load() {{\n  var n = await fetch();\n  return n + 1;\n}}\nasync function void main() {{\n  record(await load());\n}}\nmain();\nresolve();",
      new FuncBinder(_ => pending.Task),
      new ScriptNativeBindings
      {
        ["resolve"] = _ => { pending.SetResult(new IntValue(9)); return NullValue.Instance; }
      });

    var value = Assert.IsType<IntValue>(Assert.Single(recorded));
    Assert.Equal(10L, value.Value);
  }
}
