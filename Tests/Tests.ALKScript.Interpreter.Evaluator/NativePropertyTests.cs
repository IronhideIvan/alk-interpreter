using System.Collections.Generic;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Evaluator;
using ALKScript.Interpreter.Evaluator.Cursor;
using ALKScript.Interpreter.Parser;

namespace Tests.ALKScript.Interpreter.Evaluator;

public class NativePropertyTests : EvaluatorTestBase
{
  // ── Helper ───────────────────────────────────────────────────────────────────

  /// <summary>
  /// Runs source with both a "record" free function binding and native method
  /// bindings (for property accessors). Returns every value passed to record().
  /// </summary>
  private static IReadOnlyList<ALKScriptValue> RunWithNativeProperties(
    string source,
    ScriptNativeMethodBindings methodBindings)
  {
    var recorded = new List<ALKScriptValue>();
    var funcBindings = new ScriptNativeBindings
    {
      ["record"] = args => { recorded.Add(args[0]); return NullValue.Instance; }
    };
    var graph = LoadGraph(source);
    ProgramRun.Start(new CursorProgramEvaluator(funcBindings, methodBindings, null), graph).RunToCompletion();
    return recorded;
  }

  // ── Basic read-write native property ─────────────────────────────────────────

  [Fact]
  public void NativeProperty_ReadWrite_GetterAndSetterAreInvoked()
  {
    float storedValue = 0f;

    var bindings = new ScriptNativeMethodBindings
    {
      ["Sensor", "get_temperature"] = (inst, _) => new FloatValue(storedValue),
      ["Sensor", "set_temperature"] = (inst, args) =>
      {
        storedValue = (float)((FloatValue)args[0]).Value;
        return NullValue.Instance;
      }
    };

    var recorded = RunWithNativeProperties($@"{RecordDeclaration}
native class Sensor {{
    public native property float temperature {{ get; set; }}
}}
var s = new Sensor();
s.temperature = 98.6;
record(s.temperature);
", bindings);

    var value = Assert.IsType<FloatValue>(Assert.Single(recorded));
    Assert.Equal(98.6, value.Value, precision: 5);
    Assert.Equal(98.6f, storedValue, precision: 5);
  }

  // ── Get-only native property ──────────────────────────────────────────────────

  [Fact]
  public void NativeProperty_GetOnly_ReturnsHostValue()
  {
    var bindings = new ScriptNativeMethodBindings
    {
      ["Clock", "get_ticks"] = (inst, _) => new IntValue(42L)
    };

    var recorded = RunWithNativeProperties($@"{RecordDeclaration}
native class Clock {{
    public native property int ticks {{ get; }}
}}
var c = new Clock();
record(c.ticks);
", bindings);

    Assert.Equal(42L, ((IntValue)Assert.Single(recorded)).Value);
  }

  [Fact]
  public void NativeProperty_GetOnly_SetThrowsRuntimeException()
  {
    var bindings = new ScriptNativeMethodBindings
    {
      ["Clock", "get_ticks"] = (inst, _) => new IntValue(0L)
    };

    Assert.Throws<RuntimeException>(() =>
      RunWithNativeProperties($@"{RecordDeclaration}
native class Clock {{
    public native property int ticks {{ get; }}
}}
var c = new Clock();
c.ticks = 1;
", bindings));
  }

  // ── Getter receives the correct instance ─────────────────────────────────────

  [Fact]
  public void NativeProperty_Getter_ReceivesCorrectInstance()
  {
    var received = new List<InstanceValue>();

    var bindings = new ScriptNativeMethodBindings
    {
      ["Widget", "get_id"] = (inst, _) =>
      {
        received.Add(inst);
        return new IntValue(7L);
      }
    };

    RunWithNativeProperties($@"{RecordDeclaration}
native class Widget {{
    public native property int id {{ get; }}
}}
var w = new Widget();
record(w.id);
", bindings);

    var inst = Assert.Single(received);
    Assert.Equal("Widget", inst.TypeName);
  }

  // ── Missing binding raises a clear error ──────────────────────────────────────

  [Fact]
  public void NativeProperty_MissingGetterBinding_ThrowsRuntimeException()
  {
    var ex = Assert.Throws<RuntimeException>(() =>
      RunWithNativeProperties($@"{RecordDeclaration}
native class Sensor {{
    public native property float temperature {{ get; set; }}
}}
var s = new Sensor();
record(s.temperature);
", new ScriptNativeMethodBindings()));

    Assert.Contains("get_temperature", ex.Message);
  }

  // ── Parse errors ─────────────────────────────────────────────────────────────

  [Fact]
  public void NativeProperty_WithBlockBody_IsParseError()
  {
    Assert.Throws<ParseException>(() =>
      Run(@"native class Foo {
    public native property int x { get { return 0; } }
}"));
  }

  [Fact]
  public void NativeProperty_NativeOnIndividualAccessor_IsParseError()
  {
    Assert.Throws<ParseException>(() =>
      Run(@"native class Foo {
    public property int x { native get; }
}"));
  }

  [Fact]
  public void NativeProperty_OnNonNativeClass_IsParseError()
  {
    Assert.Throws<ParseException>(() =>
      Run(@"class Foo {
    public native property int x { get; set; }
}"));
  }

  [Fact]
  public void NativeProperty_AbstractCombination_IsParseError()
  {
    Assert.Throws<ParseException>(() =>
      Run(@"native abstract class Foo {
    public native abstract property int x { get; set; }
}"));
  }

  [Fact]
  public void NativeProperty_VirtualCombination_IsParseError()
  {
    Assert.Throws<ParseException>(() =>
      Run(@"native class Foo {
    public native virtual property int x { get; set; }
}"));
  }
}
