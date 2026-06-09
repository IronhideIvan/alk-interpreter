using System.Linq;
using ALKScript.Interpreter.Common.Evaluation.Values;

namespace Tests.ALKScript.Interpreter.Evaluator;

public class BreakContinueEvaluationTests : EvaluatorTestBase
{
  // ── break in while ────────────────────────────────────────────────────────

  [Fact]
  public void Evaluate_BreakInWhile_ExitsLoopImmediately()
  {
    var recorded = Run(
      $"{RecordDeclaration}" +
      "var i = 0;\n" +
      "while (i < 10) {\n" +
      "  if (i == 3) { break; }\n" +
      "  record(i);\n" +
      "  i = i + 1;\n" +
      "}");

    Assert.Equal(new long[] { 0, 1, 2 }, recorded.Select(v => Assert.IsType<IntValue>(v).Value));
  }

  [Fact]
  public void Evaluate_BreakInWhile_ExecutionContinuesAfterLoop()
  {
    var recorded = Run(
      $"{RecordDeclaration}" +
      "while (true) { break; }\n" +
      "record(99);");

    var value = Assert.IsType<IntValue>(Assert.Single(recorded));
    Assert.Equal(99L, value.Value);
  }

  // ── continue in while ─────────────────────────────────────────────────────

  [Fact]
  public void Evaluate_ContinueInWhile_SkipsRemainingBodyAndReChecksCondition()
  {
    var recorded = Run(
      $"{RecordDeclaration}" +
      "var i = 0;\n" +
      "while (i < 5) {\n" +
      "  i = i + 1;\n" +
      "  if (i == 3) { continue; }\n" +
      "  record(i);\n" +
      "}");

    Assert.Equal(new long[] { 1, 2, 4, 5 }, recorded.Select(v => Assert.IsType<IntValue>(v).Value));
  }

  // ── break in for ──────────────────────────────────────────────────────────

  [Fact]
  public void Evaluate_BreakInFor_ExitsLoopImmediately()
  {
    var recorded = Run(
      $"{RecordDeclaration}" +
      "for (var i = 0; i < 10; i = i + 1) {\n" +
      "  if (i == 4) { break; }\n" +
      "  record(i);\n" +
      "}");

    Assert.Equal(new long[] { 0, 1, 2, 3 }, recorded.Select(v => Assert.IsType<IntValue>(v).Value));
  }

  [Fact]
  public void Evaluate_BreakInFor_ExecutionContinuesAfterLoop()
  {
    var recorded = Run(
      $"{RecordDeclaration}" +
      "for (;;) { break; }\n" +
      "record(99);");

    var value = Assert.IsType<IntValue>(Assert.Single(recorded));
    Assert.Equal(99L, value.Value);
  }

  // ── continue in for ───────────────────────────────────────────────────────

  [Fact]
  public void Evaluate_ContinueInFor_SkipsBodyButStillRunsIncrement()
  {
    var recorded = Run(
      $"{RecordDeclaration}" +
      "for (var i = 0; i < 5; i = i + 1) {\n" +
      "  if (i == 2) { continue; }\n" +
      "  record(i);\n" +
      "}");

    // i == 2 is skipped; i is still incremented to 3 and the loop continues
    Assert.Equal(new long[] { 0, 1, 3, 4 }, recorded.Select(v => Assert.IsType<IntValue>(v).Value));
  }

  [Fact]
  public void Evaluate_ContinueInFor_IncrementRunsOnEveryIteration()
  {
    // Verify the increment expression runs even on the continue'd iteration,
    // by checking how many times the loop body records after a mid-run continue.
    var recorded = Run(
      $"{RecordDeclaration}" +
      "for (var i = 0; i < 6; i = i + 1) {\n" +
      "  if (i == 1 || i == 3) { continue; }\n" +
      "  record(i);\n" +
      "}");

    Assert.Equal(new long[] { 0, 2, 4, 5 }, recorded.Select(v => Assert.IsType<IntValue>(v).Value));
  }

  // ── nested loops ─────────────────────────────────────────────────────────

  [Fact]
  public void Evaluate_BreakInNestedLoop_OnlyExitsInnerLoop()
  {
    var recorded = Run(
      $"{RecordDeclaration}" +
      "for (var i = 0; i < 3; i = i + 1) {\n" +
      "  for (var j = 0; j < 3; j = j + 1) {\n" +
      "    if (j == 1) { break; }\n" +
      "    record(j);\n" +
      "  }\n" +
      "  record(i);\n" +
      "}");

    // Each outer iteration: j=0 recorded, then break; then i recorded
    Assert.Equal(new long[] { 0, 0, 0, 1, 0, 2 }, recorded.Select(v => Assert.IsType<IntValue>(v).Value));
  }

  [Fact]
  public void Evaluate_ContinueInNestedLoop_OnlyAffectsInnerLoop()
  {
    var recorded = Run(
      $"{RecordDeclaration}" +
      "for (var i = 0; i < 2; i = i + 1) {\n" +
      "  for (var j = 0; j < 3; j = j + 1) {\n" +
      "    if (j == 1) { continue; }\n" +
      "    record(j);\n" +
      "  }\n" +
      "}");

    // j == 1 skipped in each outer iteration; outer loop unaffected
    Assert.Equal(new long[] { 0, 2, 0, 2 }, recorded.Select(v => Assert.IsType<IntValue>(v).Value));
  }

  // ── interaction with try/finally ─────────────────────────────────────────

  [Fact]
  public void Evaluate_BreakInsideTryInLoop_FinallyRunsBeforeExiting()
  {
    var recorded = Run(
      $"{RecordDeclaration}" +
      "var i = 0;\n" +
      "while (i < 3) {\n" +
      "  try {\n" +
      "    if (i == 1) { break; }\n" +
      "    record(\"try\");\n" +
      "  } finally {\n" +
      "    record(\"finally\");\n" +
      "  }\n" +
      "  i = i + 1;\n" +
      "}");

    // i==0: try records, finally records; i==1: break fires, finally still runs
    Assert.Equal(
      new[] { "try", "finally", "finally" },
      recorded.Select(v => Assert.IsType<StringValue>(v).Value));
  }

  [Fact]
  public void Evaluate_ContinueInsideTryInLoop_FinallyRunsBeforeNextIteration()
  {
    var recorded = Run(
      $"{RecordDeclaration}" +
      "for (var i = 0; i < 3; i = i + 1) {\n" +
      "  try {\n" +
      "    if (i == 1) { continue; }\n" +
      "    record(\"try\");\n" +
      "  } finally {\n" +
      "    record(\"finally\");\n" +
      "  }\n" +
      "}");

    // i==0: try+finally; i==1: continue, finally still runs; i==2: try+finally
    Assert.Equal(
      new[] { "try", "finally", "finally", "try", "finally" },
      recorded.Select(v => Assert.IsType<StringValue>(v).Value));
  }
}
