using System.Collections.Generic;
using System.Threading.Tasks;
using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Common.Evaluation;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Common.Token;
using ALKScript.Interpreter.Evaluator;

namespace Tests.ALKScript.Interpreter.Evaluator.Unit;

public class BreakContinueExecutorTests
{
  private static StatementExecutor MakeExecutor(FakeEvaluationContext context) =>
    new StatementExecutor(context, new FunctionValueFactory());

  // ── signal emission ───────────────────────────────────────────────────────

  [Fact]
  public async Task Execute_BreakStatement_SetsBreakSignal()
  {
    var context = new FakeEvaluationContext();

    await MakeExecutor(context).Execute(
      new BreakStmt(Nodes.Token(ALKScriptTokenType.Break, "break")),
      new ScriptEnvironment());

    Assert.NotNull(context.Signal);
    Assert.Equal(SignalKind.Break, context.Signal!.Value.Kind);
  }

  [Fact]
  public async Task Execute_ContinueStatement_SetsContinueSignal()
  {
    var context = new FakeEvaluationContext();

    await MakeExecutor(context).Execute(
      new ContinueStmt(Nodes.Token(ALKScriptTokenType.Continue, "continue")),
      new ScriptEnvironment());

    Assert.NotNull(context.Signal);
    Assert.Equal(SignalKind.Continue, context.Signal!.Value.Kind);
  }

  // ── while loop ────────────────────────────────────────────────────────────

  [Fact]
  public async Task Execute_WhileLoop_BreakSignal_ExitsLoopAndClearsSignal()
  {
    // Loop runs: iteration 0 records, iteration 1 breaks.
    // After the while completes the Break signal must be consumed (null).
    var context = new FakeEvaluationContext();
    int iterations = 0;
    context.EvalImpl = (_, _) =>
    {
      iterations++;
      if (iterations >= 2)
      {
        context.Signal = Signal.Break();
      }
      return BoolValue.Of(true); // condition always true — break controls exit
    };

    var whileStmt = new WhileStmt(
      condition: Nodes.Literal(true),
      body: new ExpressionStmt(Nodes.Literal("tick")));

    await MakeExecutor(context).Execute(whileStmt, new ScriptEnvironment());

    Assert.Null(context.Signal);
    Assert.Equal(2, iterations);
  }

  [Fact]
  public async Task Execute_WhileLoop_ContinueSignal_ClearsSignalAndLoopsAgain()
  {
    // Body raises Continue on iteration 1; loop must keep running (condition
    // checked again) until the condition evaluates to false on iteration 3.
    var context = new FakeEvaluationContext();
    int conditionChecks = 0;
    context.EvalImpl = (expr, _) =>
    {
      if (expr is LiteralExpr { Value: "condition" })
      {
        conditionChecks++;
        return BoolValue.Of(conditionChecks <= 3);
      }

      if (conditionChecks == 1)
      {
        context.Signal = Signal.Continue();
      }

      return NullValue.Instance;
    };

    var whileStmt = new WhileStmt(
      condition: Nodes.Literal("condition"),
      body: new ExpressionStmt(Nodes.Literal("body")));

    await MakeExecutor(context).Execute(whileStmt, new ScriptEnvironment());

    Assert.Null(context.Signal);
    Assert.Equal(4, conditionChecks); // 3 true + 1 false
  }

  [Fact]
  public async Task Execute_WhileLoop_NonLoopSignal_PropagatesOutOfLoop()
  {
    // A Return signal raised inside the body must propagate out unchanged —
    // the while loop must not consume it.
    var context = new FakeEvaluationContext();
    context.EvalImpl = (_, _) =>
    {
      context.Signal = Signal.Return(new IntValue(42));
      return BoolValue.Of(true);
    };

    var whileStmt = new WhileStmt(
      condition: Nodes.Literal(true),
      body: new ExpressionStmt(Nodes.Literal("body")));

    await MakeExecutor(context).Execute(whileStmt, new ScriptEnvironment());

    Assert.NotNull(context.Signal);
    Assert.Equal(SignalKind.Return, context.Signal!.Value.Kind);
  }

  // ── for loop ──────────────────────────────────────────────────────────────

  [Fact]
  public async Task Execute_ForLoop_BreakSignal_ExitsLoopAndClearsSignal()
  {
    var context = new FakeEvaluationContext();
    int bodyRuns = 0;
    context.EvalImpl = (expr, _) =>
    {
      if (expr is LiteralExpr { Value: "condition" }) return BoolValue.Of(true);
      if (expr is LiteralExpr { Value: "body" })
      {
        bodyRuns++;
        if (bodyRuns >= 2) context.Signal = Signal.Break();
      }
      return NullValue.Instance;
    };

    var forStmt = new ForStmt(
      initializer: null,
      condition: Nodes.Literal("condition"),
      increment: Nodes.Literal("increment"),
      body: new ExpressionStmt(Nodes.Literal("body")));

    await MakeExecutor(context).Execute(forStmt, new ScriptEnvironment());

    Assert.Null(context.Signal);
    Assert.Equal(2, bodyRuns);
  }

  [Fact]
  public async Task Execute_ForLoop_ContinueSignal_RunsIncrementThenLoopsAgain()
  {
    // Continue must still run the increment before re-checking the condition.
    var context = new FakeEvaluationContext();
    int conditionChecks = 0;
    int incrementRuns = 0;
    context.EvalImpl = (expr, _) =>
    {
      if (expr is LiteralExpr { Value: "condition" })
      {
        conditionChecks++;
        return BoolValue.Of(conditionChecks <= 3);
      }
      if (expr is LiteralExpr { Value: "increment" })
      {
        incrementRuns++;
        return NullValue.Instance;
      }
      // body: raise Continue on the first iteration
      if (conditionChecks == 1) context.Signal = Signal.Continue();
      return NullValue.Instance;
    };

    var forStmt = new ForStmt(
      initializer: null,
      condition: Nodes.Literal("condition"),
      increment: Nodes.Literal("increment"),
      body: new ExpressionStmt(Nodes.Literal("body")));

    await MakeExecutor(context).Execute(forStmt, new ScriptEnvironment());

    Assert.Null(context.Signal);
    Assert.Equal(4, conditionChecks);   // 3 true + 1 false
    Assert.Equal(3, incrementRuns);     // runs on every iteration, including the continue one
  }

  [Fact]
  public async Task Execute_ForLoop_NonLoopSignal_PropagatesOutOfLoop()
  {
    var context = new FakeEvaluationContext();
    context.EvalImpl = (_, _) =>
    {
      context.Signal = Signal.Return(NullValue.Instance);
      return BoolValue.Of(true);
    };

    var forStmt = new ForStmt(
      initializer: null,
      condition: Nodes.Literal("condition"),
      increment: null,
      body: new ExpressionStmt(Nodes.Literal("body")));

    await MakeExecutor(context).Execute(forStmt, new ScriptEnvironment());

    Assert.NotNull(context.Signal);
    Assert.Equal(SignalKind.Return, context.Signal!.Value.Kind);
  }
}
