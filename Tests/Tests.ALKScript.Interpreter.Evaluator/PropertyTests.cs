using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Evaluator;

namespace Tests.ALKScript.Interpreter.Evaluator;

public class PropertyTests : EvaluatorTestBase
{
  // ── Auto-property read/write ─────────────────────────────────────────────────

  [Fact]
  public void Property_AutoReadWrite_GetAndSet()
  {
    var recorded = Run($@"{RecordDeclaration}
class Point {{
    public property int x {{ get; set; }}
    public property int y {{ get; set; }}
    public new(int x, int y) {{
        this.x = x;
        this.y = y;
    }}
}}
var p = new Point(3, 4);
record(p.x);
record(p.y);
p.x = 10;
record(p.x);
");
    Assert.Equal(3L, ((IntValue)recorded[0]).Value);
    Assert.Equal(4L, ((IntValue)recorded[1]).Value);
    Assert.Equal(10L, ((IntValue)recorded[2]).Value);
  }

  // ── Get-only auto-property ───────────────────────────────────────────────────

  [Fact]
  public void Property_GetOnlyAuto_CanBeSetInConstructorOnly()
  {
    var recorded = Run($@"{RecordDeclaration}
class Immutable {{
    public property int value {{ get; }}
    public new(int v) {{
        this.value = v;
    }}
}}
var obj = new Immutable(42);
record(obj.value);
");
    Assert.Equal(42L, ((IntValue)Assert.Single(recorded)).Value);
  }

  [Fact]
  public void Property_GetOnlyAuto_CannotBeSetOutsideConstructor()
  {
    Assert.Throws<RuntimeException>(() => Run($@"{RecordDeclaration}
class Immutable {{
    public property int value {{ get; }}
    public new(int v) {{
        this.value = v;
    }}
    public function void setIt(int v) {{
        this.value = v;
    }}
}}
var obj = new Immutable(5);
obj.setIt(10);
"));
  }

  // ── Full property with body ──────────────────────────────────────────────────

  [Fact]
  public void Property_FullGetterSetter_InvokesBody()
  {
    var recorded = Run($@"{RecordDeclaration}
class Counter {{
    private int _count = 0;
    public property int count {{
        get {{ return this._count; }}
        set {{ this._count = value; }}
    }}
}}
var c = new Counter();
record(c.count);
c.count = 5;
record(c.count);
");
    Assert.Equal(0L, ((IntValue)recorded[0]).Value);
    Assert.Equal(5L, ((IntValue)recorded[1]).Value);
  }

  [Fact]
  public void Property_GetterWithComputation_ReturnsComputedValue()
  {
    var recorded = Run($@"{RecordDeclaration}
class Circle {{
    public float radius;
    public new(float r) {{ this.radius = r; }}
    public property float diameter {{
        get {{ return this.radius * 2.0; }}
    }}
}}
var c = new Circle(5.0);
record(c.diameter);
");
    Assert.Equal(10.0, ((FloatValue)Assert.Single(recorded)).Value);
  }

  // ── No getter ────────────────────────────────────────────────────────────────

  [Fact]
  public void Property_NoGetter_ThrowsOnRead()
  {
    Assert.Throws<RuntimeException>(() => Run($@"{RecordDeclaration}
class Sink {{
    private int _v = 0;
    public property int v {{
        set {{ this._v = value; }}
    }}
}}
var s = new Sink();
record(s.v);
"));
  }

  // ── Interface property ───────────────────────────────────────────────────────

  [Fact]
  public void Property_InterfaceProperty_ClassSatisfiesInterface()
  {
    var recorded = Run($@"{RecordDeclaration}
interface IHasName {{
    property string name {{ get; }}
}}
class Person implements IHasName {{
    public property string name {{ get; set; }}
    public new(string n) {{ this.name = n; }}
}}
var p = new Person(""Alice"");
var q = p is IHasName;
record(q);
record(p.name);
");
    Assert.Equal(BoolValue.True, recorded[0]);
    Assert.Equal("Alice", ((StringValue)recorded[1]).Value);
  }

  // ── Static property ──────────────────────────────────────────────────────────

  [Fact]
  public void Property_StaticAutoProperty_GetAndSet()
  {
    var recorded = Run($@"{RecordDeclaration}
class Config {{
    public static property int maxSize {{ get; set; }}
}}
Config.maxSize = 100;
record(Config.maxSize);
");
    Assert.Equal(100L, ((IntValue)Assert.Single(recorded)).Value);
  }
}
