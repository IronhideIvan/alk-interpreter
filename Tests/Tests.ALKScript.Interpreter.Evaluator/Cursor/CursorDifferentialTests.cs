using System.Collections.Generic;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Evaluator;
using ALKScript.Interpreter.Evaluator.Cursor;

namespace Tests.ALKScript.Interpreter.Evaluator.Cursor;

/// <summary>
/// Step 9 differential coverage for the cursor-rewrite plan (docs:
/// validated-nibbling-narwhal): for a representative cross-section of the
/// node types and scripts already covered by
/// <c>Tests.ALKScript.Interpreter.Evaluator</c>'s existing Task/<see cref="ALKScript.Interpreter.Evaluator.Scheduling.ScriptScheduler"/>-based
/// tests, confirm <see cref="ALKScript.Interpreter.Evaluator.Cursor.CursorProgramEvaluator"/>
/// produces the same <c>record()</c> output as the existing
/// <see cref="ALKScript.Interpreter.Evaluator.ProgramEvaluator"/> via
/// <see cref="CursorEvaluatorTestBase.AssertSameResult"/>.
///
/// Out of scope for this differential suite (per the plan's §4/§6
/// restrictions, not yet supported by the cursor evaluator and covered
/// instead by <c>Cursor/CursorAwaitExecutorTests.cs</c> and
/// <c>Cursor/CursorProgramEvaluatorTests.cs</c> for the cases that *are*
/// in scope):
/// - <c>await</c> in a disallowed expression position with a genuinely
///   unresolved <c>thunk</c> (§4).
/// - <c>await [a, b, ...]</c> ("whenAll") where any element is a
///   genuinely in-flight (non-replayed, not-yet-completed) operation (§6) —
///   e.g. <c>Evaluate_AwaitOnArrayOfPendingOperations_*</c>,
///   <c>Evaluate_AwaitOnArrayWhereOneFaults_*</c>,
///   <c>Evaluate_AwaitOnArrayWhereBothFault_*</c>,
///   <c>Evaluate_AwaitOnArrayFault_*</c> in <c>AsyncEvaluationTests.cs</c>.
/// - Native array-method callbacks (map/filter/etc.) that themselves
///   <c>await</c> (plan §7) — not used by any existing test.
/// </summary>
public class CursorDifferentialTests : CursorEvaluatorTestBase
{
  [Fact]
  public void Arithmetic_MixedIntAndFloat()
  {
    AssertSameResult($"{RecordDeclaration} record(1 + 2.5); record(2 * 3 - 1); record(10 / 4); record(10 % 3);");
  }

  [Fact]
  public void StringConcatenation_WithNonStringOperands()
  {
    AssertSameResult($"{RecordDeclaration} record(\"a\" + \"b\" + 1);");
  }

  [Fact]
  public void Comparisons_AndUnaryNegation()
  {
    AssertSameResult($"{RecordDeclaration} record(1 < 2); record(2 <= 2); record(3 > 5); record(-5); record(!false);");
  }

  [Fact]
  public void TernaryExpression()
  {
    AssertSameResult($"{RecordDeclaration} var x = 7; record(x > 5 ? \"big\" : \"small\");");
  }

  [Fact]
  public void LogicalShortCircuit_DoesNotEvaluateRightOperand()
  {
    AssertSameResult($"{RecordDeclaration}\nfunction bool sideEffect() {{\n  record(\"called\");\n  return true;\n}}\nrecord(false && sideEffect());\nrecord(true || sideEffect());");
  }

  [Fact]
  public void TemplateLiterals_WithInterpolation()
  {
    AssertSameResult($"{RecordDeclaration}\nvar a = 2;\nvar b = 3;\nrecord(`${{a}} + ${{b}} = ${{a + b}}`);");
  }

  [Fact]
  public void Arrays_IndexingAndAssignment()
  {
    AssertSameResult($"{RecordDeclaration}\nvar items = [1, 2, 3];\nrecord(items);\nrecord(items[1]);\nitems[0] = 9;\nrecord(items[0]);\nrecord(items.length);");
  }

  [Fact]
  public void IfElse_BothBranches()
  {
    AssertSameResult($"{RecordDeclaration}\nif (1 < 2) {{\n  record(\"then\");\n}} else {{\n  record(\"else\");\n}}\nif (1 > 2) {{\n  record(\"then2\");\n}} else {{\n  record(\"else2\");\n}}");
  }

  [Fact]
  public void WhileLoop_AccumulatesValue()
  {
    AssertSameResult($"{RecordDeclaration}\nvar i = 0;\nvar sum = 0;\nwhile (i < 5) {{\n  sum = sum + i;\n  i = i + 1;\n}}\nrecord(sum);");
  }

  [Fact]
  public void DoWhileLoop_RunsAtLeastOnce()
  {
    AssertSameResult($"{RecordDeclaration}\nvar i = 10;\nvar count = 0;\ndo {{\n  count = count + 1;\n  i = i + 1;\n}} while (i < 10);\nrecord(count);");
  }

  [Fact]
  public void ForLoop_WithBreakAndContinue()
  {
    AssertSameResult($"{RecordDeclaration}\nfor (var i = 0; i < 10; i = i + 1) {{\n  if (i == 2) {{ continue; }}\n  if (i == 5) {{ break; }}\n  record(i);\n}}");
  }

  [Fact]
  public void ForeachLoop_OverArray()
  {
    AssertSameResult($"{RecordDeclaration}\nvar items = [10, 20, 30];\nvar total = 0;\nforeach (var item in items) {{\n  total = total + item;\n}}\nrecord(total);");
  }

  [Fact]
  public void SwitchStatement_MatchesCaseAndDefault()
  {
    AssertSameResult($"{RecordDeclaration}\nfunction string classify(int n) {{\n  switch (n) {{\n    case 1:\n      return \"one\";\n    case 2:\n      return \"two\";\n    default:\n      return \"many\";\n  }}\n}}\nrecord(classify(1));\nrecord(classify(2));\nrecord(classify(9));");
  }

  [Fact]
  public void TryCatchFinally_RunsFinallyAndCatchesThrow()
  {
    AssertSameResult($"{RecordDeclaration}\ntry {{\n  throw \"boom\";\n}} catch (string e) {{\n  record(e);\n}} finally {{\n  record(\"finally\");\n}}");
  }

  [Fact]
  public void FunctionCalls_WithRecursion()
  {
    AssertSameResult($"{RecordDeclaration}\nfunction int factorial(int n) {{\n  if (n <= 1) {{ return 1; }}\n  return n * factorial(n - 1);\n}}\nrecord(factorial(5));");
  }

  [Fact]
  public void Classes_FieldsConstructorsAndMethods()
  {
    AssertSameResult($"{RecordDeclaration}\nclass Point {{\n  public int x;\n  public int y;\n  new(int x, int y) {{\n    this.x = x;\n    this.y = y;\n  }}\n  public function int sum() {{\n    return this.x + this.y;\n  }}\n}}\nvar p = new Point(1, 2);\nrecord(p.x);\nrecord(p.y);\nrecord(p.sum());");
  }

  [Fact]
  public void Classes_InheritanceAndOverriding()
  {
    AssertSameResult($"{RecordDeclaration}\nclass Animal {{\n  public function string speak() {{\n    return \"...\";\n  }}\n}}\nclass Dog extends Animal {{\n  public function string speak() {{\n    return \"woof\";\n  }}\n}}\nvar d = new Dog();\nrecord(d.speak());");
  }

  [Fact]
  public void Classes_StaticFields()
  {
    AssertSameResult($"{RecordDeclaration}\nclass Counter {{\n  public static int total = 41;\n}}\nCounter.total = Counter.total + 1;\nrecord(Counter.total);");
  }

  [Fact]
  public void Enums_MembersAndValues()
  {
    AssertSameResult($"{RecordDeclaration}\nenum Color {{ Red, Green, Blue }}\nrecord(Color.Green);");
  }

  [Fact]
  public void NativeBindings_AreInvokedWithArguments()
  {
    var source = $"{RecordDeclaration}\nnative function int addOne(int n);\nrecord(addOne(41));";

    var bindings = new ScriptNativeBindings
    {
      ["addOne"] = arguments => new IntValue(((IntValue)arguments[0]).Value + 1)
    };

    var oldRecorded = new List<ALKScriptValue>();
    RunWithBindings(source, new ScriptNativeBindings(bindings)
    {
      ["record"] = arguments => { oldRecorded.Add(arguments[0]); return NullValue.Instance; }
    });

    var newRecorded = new List<ALKScriptValue>();
    var newBindings = new ScriptNativeBindings(bindings)
    {
      ["record"] = arguments => { newRecorded.Add(arguments[0]); return NullValue.Instance; }
    };
    var graph = LoadGraph(source);
    var result = new CursorProgramEvaluator(newBindings).Evaluate(graph);
    Assert.Equal(ProgramRunResult.Completed, result);

    Assert.Equal(oldRecorded.Count, newRecorded.Count);
    for (int i = 0; i < oldRecorded.Count; i++)
    {
      Assert.Equal(oldRecorded[i].ToString(), newRecorded[i].ToString());
    }
  }

  [Fact]
  public void FunctionBody_AwaitOnAlreadyResolvedValue_DoesNotSuspend()
  {
    AssertSameResult($"{RecordDeclaration}\nfunction int foo() {{\n  return await 1;\n}}\nrecord(foo());");
  }

  [Fact]
  public void GlobalPreludeDeclarations_AreVisibleToTheEntryModule()
  {
    var globalPreludes = new[] { "function int answer() { return 42; }" };
    var source = $"{RecordDeclaration}\nrecord(answer());";

    var oldRecorded = new List<ALKScriptValue>();
    RunWithGlobals(source, globalPreludes, new ScriptNativeBindings
    {
      ["record"] = arguments => { oldRecorded.Add(arguments[0]); return NullValue.Instance; }
    });

    var actual = RunCursor(source, globalPreludes);

    Assert.Equal(oldRecorded.Count, actual.Count);
    for (int i = 0; i < oldRecorded.Count; i++)
    {
      Assert.Equal(oldRecorded[i].ToString(), actual[i].ToString());
    }
  }
}
