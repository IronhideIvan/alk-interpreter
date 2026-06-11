using System.Collections.Generic;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Evaluator;
using ALKScript.Interpreter.Evaluator.Cursor;

namespace Tests.ALKScript.Interpreter.Evaluator.Cursor;

/// <summary>
/// Cursor-evaluator counterpart to <see cref="EvaluatorTestBase.Run"/>: runs
/// <paramref name="source"/> to completion via <see cref="CursorProgramEvaluator"/>
/// instead of the Task/<see cref="ALKScript.Interpreter.Evaluator.Scheduling.ScriptScheduler"/>-based
/// <see cref="ProgramEvaluator"/>, and returns every value passed to the
/// host-bound <c>record()</c> native (see <see cref="EvaluatorTestBase.RecordDeclaration"/>),
/// in call order. Used by the Step 9 differential test suite (docs:
/// validated-nibbling-narwhal) to confirm the cursor evaluator produces the
/// same observable results as the existing evaluator for in-scope scripts.
///
/// A genuinely <c>Awaiting</c> result is treated as a test failure here —
/// every differential case must run to completion synchronously.
/// </summary>
public abstract class CursorEvaluatorTestBase : EvaluatorTestBase
{
  protected static IReadOnlyList<ALKScriptValue> RunCursor(string source, IReadOnlyList<string>? globalPreludeSources = null)
  {
    var recorded = new List<ALKScriptValue>();
    var bindings = new ScriptNativeBindings
    {
      ["record"] = arguments => { recorded.Add(arguments[0]); return NullValue.Instance; }
    };

    var graph = LoadGraph(source, globalPreludeSources);
    var evaluator = new CursorProgramEvaluator(bindings);

    var result = evaluator.Evaluate(graph);
    Assert.Equal(ProgramRunResult.Completed, result);

    return recorded;
  }

  /// <summary>
  /// Runs <paramref name="source"/> through both the existing
  /// <see cref="EvaluatorTestBase.Run"/> and <see cref="RunCursor"/>, asserting
  /// that both produce the same sequence of <c>record()</c> values (compared
  /// via <c>"{TypeName}:{value}"</c>, since neither evaluator's value types
  /// implement value equality).
  /// </summary>
  protected static void AssertSameResult(string source)
  {
    var expected = Run(source);
    var actual = RunCursor(source);

    Assert.Equal(Describe(expected), Describe(actual));
  }

  private static List<string> Describe(IReadOnlyList<ALKScriptValue> values)
  {
    var described = new List<string>();
    foreach (var value in values)
    {
      described.Add($"{value.TypeName}:{value}");
    }
    return described;
  }
}
