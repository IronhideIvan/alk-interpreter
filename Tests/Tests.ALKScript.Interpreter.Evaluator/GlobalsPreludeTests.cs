using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Evaluator;

namespace Tests.ALKScript.Interpreter.Evaluator;

/// <summary>
/// Covers <see cref="ProgramEvaluator"/>'s "global prelude" injection point:
/// runtime-supplied ALKScript source(s), compiled and executed into the root
/// environment before the entry module runs, giving scripts "true global"
/// bindings — callable with no <c>import</c> and no per-script re-declaration.
///
/// The evaluator ships with no prelude content of its own (see the
/// constructor docs on <see cref="ProgramEvaluator"/>) — deciding what's
/// globally available, e.g. a <c>print</c>, is entirely the runtime's call.
/// These tests stand in for "the runtime" by passing prelude source strings
/// directly via <see cref="EvaluatorTestBase.RunWithGlobals"/>.
/// </summary>
public class GlobalsPreludeTests : EvaluatorTestBase
{
  private const string PrintPrelude = "native function void print(Object value);\n";

  [Fact]
  public void Evaluate_RuntimeSuppliedGlobal_IsCallableWithoutAnyDeclarationOrImport()
  {
    var calls = new List<ALKScriptValue>();

    RunWithGlobals(
      "print(\"hi\");\nprint(42);",
      new[] { PrintPrelude },
      new ScriptNativeBindings
      {
        ["print"] = arguments =>
        {
          calls.Add(arguments[0]);
          return NullValue.Instance;
        }
      });

    Assert.Equal(2, calls.Count);
    Assert.Equal("hi", Assert.IsType<StringValue>(calls[0]).Value);
    Assert.Equal(42L, Assert.IsType<IntValue>(calls[1]).Value);
  }

  [Fact]
  public void Evaluate_RuntimeSuppliedGlobal_EnforcesItsDeclaredArityLikeAnyOtherFunction()
  {
    var exception = Assert.Throws<RuntimeException>(() =>
      RunWithGlobals(
        "print(\"too\", \"many\");",
        new[] { PrintPrelude },
        new ScriptNativeBindings { ["print"] = _ => NullValue.Instance }));

    Assert.Contains("Expected 1 argument(s) but got 2", exception.Message);
  }

  [Fact]
  public void Evaluate_TopLevelDeclarationCanShadowARuntimeSuppliedGlobal()
  {
    var globalCalls = new List<string>();
    var recorded = new List<ALKScriptValue>();

    RunWithGlobals(
      RecordDeclaration +
      "function void print(string message) {\n  record(message);\n}\n" +
      "print(\"shadowed\");\n",
      new[] { PrintPrelude },
      new ScriptNativeBindings
      {
        ["print"] = _ =>
        {
          globalCalls.Add("global");
          return NullValue.Instance;
        },
        ["record"] = arguments =>
        {
          recorded.Add(arguments[0]);
          return NullValue.Instance;
        }
      });

    Assert.Empty(globalCalls);
    var value = Assert.IsType<StringValue>(Assert.Single(recorded));
    Assert.Equal("shadowed", value.Value);
  }

  [Fact]
  public void Evaluate_MultiplePreludeSources_RunInOrderAndShareTheRootEnvironment()
  {
    var recorded = new List<ALKScriptValue>();

    RunWithGlobals(
      RecordDeclaration + "record(triple(7));",
      new[]
      {
        // Second source's "triple" closes over the first source's "double" —
        // proving both run into the same shared root environment, in order.
        "native function int double(int n);\n",
        "function int triple(int n) { return double(n) + n; }\n"
      },
      new ScriptNativeBindings
      {
        ["double"] = arguments => new IntValue(((IntValue)arguments[0]).Value * 2),
        ["record"] = arguments =>
        {
          recorded.Add(arguments[0]);
          return NullValue.Instance;
        }
      });

    var value = Assert.IsType<IntValue>(Assert.Single(recorded));
    Assert.Equal(21L, value.Value);
  }

  [Fact]
  public void Evaluate_WithNoPreludeSourcesSupplied_LeavesTheGlobalScopeEmpty()
  {
    var exception = Assert.Throws<RuntimeException>(() => Run("print(\"unregistered\");"));

    Assert.Contains("Undefined name 'print'", exception.Message);
  }
}
