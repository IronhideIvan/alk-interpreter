using ALKScript.Interpreter.Common.Evaluation;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Evaluator;
using ALKScript.Interpreter.Runtime;

namespace Tests.ALKScript.Interpreter.Runtime;

public class ScriptArgumentsTests
{
  [Fact]
  public void RunFromSource_WithArguments_SeedsValuesAsRootVariables()
  {
    var recorded = new List<ALKScriptValue>();

    var runtime = new ProgramRuntime();
    runtime.NativeBindings["record"] = args => { recorded.Add(args[0]); return NullValue.Instance; };

    runtime.RunFromSource(
      "native function void record(Object v);\nrecord(entityId);",
      new ScriptArguments { ["entityId"] = new StringValue("enemy-42") }
    ).RunToCompletion();

    var value = Assert.IsType<StringValue>(Assert.Single(recorded));
    Assert.Equal("enemy-42", value.Value);
  }

  [Fact]
  public void RunFromSource_ArgumentIsReadOnly_ThrowsOnReassignment()
  {
    var runtime = new ProgramRuntime();

    Assert.Throws<RuntimeException>(() =>
      runtime.RunFromSource(
        "entityId = \"other\";",
        new ScriptArguments { ["entityId"] = new StringValue("original") }
      ).RunToCompletion());
  }

  [Fact]
  public void RunFromSource_NoArguments_ProgramRunsNormally()
  {
    var recorded = new List<ALKScriptValue>();

    var runtime = new ProgramRuntime();
    runtime.NativeBindings["record"] = args => { recorded.Add(args[0]); return NullValue.Instance; };

    runtime.RunFromSource("native function void record(Object v);\nrecord(1);").RunToCompletion();

    var value = Assert.IsType<IntValue>(Assert.Single(recorded));
    Assert.Equal(1L, value.Value);
  }

  [Fact]
  public void RunFromGraph_WithArguments_SeedsValuesAsRootVariables()
  {
    var recorded = new List<ALKScriptValue>();

    var runtime = new ProgramRuntime();
    runtime.NativeBindings["record"] = args => { recorded.Add(args[0]); return NullValue.Instance; };

    var graph = runtime.LoadFromSource("native function void record(Object v);\nrecord(entityId);");
    runtime.RunFromGraph(graph, new ScriptArguments { ["entityId"] = new IntValue(7) }).RunToCompletion();

    var value = Assert.IsType<IntValue>(Assert.Single(recorded));
    Assert.Equal(7L, value.Value);
  }

  [Fact]
  public void RunFromGraph_SameGraphDifferentArguments_ProducesIndependentRuns()
  {
    var calls = new List<long>();

    var runtime = new ProgramRuntime();
    runtime.NativeBindings["record"] = args => { calls.Add(((IntValue)args[0]).Value); return NullValue.Instance; };

    var graph = runtime.LoadFromSource("native function void record(Object v);\nrecord(n);");
    runtime.RunFromGraph(graph, new ScriptArguments { ["n"] = new IntValue(1) }).RunToCompletion();
    runtime.RunFromGraph(graph, new ScriptArguments { ["n"] = new IntValue(2) }).RunToCompletion();

    Assert.Equal(new[] { 1L, 2L }, calls);
  }
}
