using System;
using System.IO;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Evaluator;

namespace Tests.ALKScript.Interpreter.Evaluator;

/// <summary>
/// Covers the reserved "globals.alk" prelude (<see cref="GlobalsSource"/>):
/// ordinary top-level declarations executed into the root environment before
/// the entry module runs, giving every script "true global" bindings — e.g.
/// <c>print(...)</c> — callable with no <c>import</c> and no per-script
/// <c>native function</c> re-declaration. The host still supplies the host
/// implementation through the same <see cref="ScriptNativeBindings"/> table
/// used for a script's own <c>native</c> declarations.
/// </summary>
public class GlobalsPreludeTests : EvaluatorTestBase
{
  [Fact]
  public void Evaluate_Print_IsCallableWithoutAnyDeclarationOrImport()
  {
    var calls = new List<ALKScriptValue>();

    RunWithBindings(
      "print(\"hi\");\nprint(42);",
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
  public void Evaluate_Print_EnforcesItsDeclaredArityLikeAnyOtherFunction()
  {
    var exception = Assert.Throws<RuntimeException>(() =>
      RunWithBindings(
        "print(\"too\", \"many\");",
        new ScriptNativeBindings { ["print"] = _ => NullValue.Instance }));

    Assert.Contains("Expected 1 argument(s) but got 2", exception.Message);
  }

  [Fact]
  public void Evaluate_TopLevelDeclarationCanShadowAGlobalBinding()
  {
    var globalCalls = new List<string>();
    var recorded = new List<ALKScriptValue>();

    RunWithBindings(
      RecordDeclaration +
      "function void print(string message) {\n  record(message);\n}\n" +
      "print(\"shadowed\");\n",
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
  public void Evaluate_PrintWithoutAHostOverride_FallsBackToTheDefaultConsoleImplementation()
  {
    var originalOut = Console.Out;

    try
    {
      using var captured = new StringWriter();
      Console.SetOut(captured);

      // No "print" entry supplied — the prelude's default binding should be
      // used instead of failing at declaration time, so scripts that don't
      // care about output don't force every host to register one.
      RunWithBindings("print(\"from default\");", new ScriptNativeBindings());

      Assert.Contains("from default", captured.ToString());
    }
    finally
    {
      Console.SetOut(originalOut);
    }
  }

  [Fact]
  public void Evaluate_HostSuppliedPrintBinding_OverridesTheDefault()
  {
    var calls = new List<string>();

    RunWithBindings(
      "print(\"overridden\");",
      new ScriptNativeBindings
      {
        ["print"] = arguments =>
        {
          calls.Add(((StringValue)arguments[0]).Value);
          return NullValue.Instance;
        }
      });

    Assert.Equal(new[] { "overridden" }, calls);
  }
}
