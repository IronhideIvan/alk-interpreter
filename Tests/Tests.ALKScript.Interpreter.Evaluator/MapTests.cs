using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Evaluator;

namespace Tests.ALKScript.Interpreter.Evaluator;

public class MapTests : EvaluatorTestBase
{
  // ── Construction ────────────────────────────────────────────────────────────

  [Fact]
  public void Map_EmptyLiteral_CreatesEmptyMap()
  {
    var recorded = Run($"{RecordDeclaration}var m = new map<string, int> {{}};\nrecord(m.has(\"a\"));");
    Assert.Equal(BoolValue.False, Assert.Single(recorded));
  }

  [Fact]
  public void Map_StringKeyLiteral_CanRetrieveValues()
  {
    var recorded = Run($"{RecordDeclaration}var m = new map<string, int> {{ \"apples\": 3, \"bananas\": 7 }};\nrecord(m[\"apples\"]);\nrecord(m[\"bananas\"]);");
    Assert.Equal(2, recorded.Count);
    Assert.Equal(3L, ((IntValue)recorded[0]).Value);
    Assert.Equal(7L, ((IntValue)recorded[1]).Value);
  }

  [Fact]
  public void Map_IntKeyLiteral_CanRetrieveValues()
  {
    var recorded = Run($"{RecordDeclaration}var m = new map<int, string> {{ 1: \"one\", 2: \"two\" }};\nrecord(m[1]);\nrecord(m[2]);");
    Assert.Equal("one", ((StringValue)recorded[0]).Value);
    Assert.Equal("two", ((StringValue)recorded[1]).Value);
  }

  // ── Indexer get/set ──────────────────────────────────────────────────────────

  [Fact]
  public void Map_IndexerSet_InsertsAndUpdates()
  {
    var recorded = Run($"{RecordDeclaration}var m = new map<string, int> {{ \"x\": 1 }};\nm[\"x\"] = 10;\nm[\"y\"] = 20;\nrecord(m[\"x\"]);\nrecord(m[\"y\"]);");
    Assert.Equal(10L, ((IntValue)recorded[0]).Value);
    Assert.Equal(20L, ((IntValue)recorded[1]).Value);
  }

  [Fact]
  public void Map_GetMissingKey_ThrowsRuntimeException()
  {
    Assert.Throws<RuntimeException>(() =>
      Run($"{RecordDeclaration}var m = new map<string, int> {{}};\nrecord(m[\"missing\"]);"));
  }

  [Fact]
  public void Map_GetMissingKey_IsCatchableByScriptTryCatch()
  {
    var recorded = Run($@"{RecordDeclaration}
var m = new map<string, int> {{}};
var caught = false;
try {{
    var x = m[""missing""];
}} catch {{
    caught = true;
}}
record(caught);
");
    Assert.Equal(BoolValue.True, Assert.Single(recorded));
  }

  // ── Methods ──────────────────────────────────────────────────────────────────

  [Fact]
  public void Map_Has_ReturnsTrueForExistingKey()
  {
    var recorded = Run($"{RecordDeclaration}var m = new map<string, int> {{ \"a\": 1 }};\nrecord(m.has(\"a\"));\nrecord(m.has(\"b\"));");
    Assert.Equal(BoolValue.True, recorded[0]);
    Assert.Equal(BoolValue.False, recorded[1]);
  }

  [Fact]
  public void Map_Add_InsertsNewKey()
  {
    var recorded = Run($"{RecordDeclaration}var m = new map<string, int> {{}};\nm.add(\"k\", 42);\nrecord(m[\"k\"]);");
    Assert.Equal(42L, ((IntValue)Assert.Single(recorded)).Value);
  }

  [Fact]
  public void Map_Add_DuplicateKey_ThrowsRuntimeException()
  {
    Assert.Throws<RuntimeException>(() =>
      Run($"{RecordDeclaration}var m = new map<string, int> {{ \"a\": 1 }};\nm.add(\"a\", 2);"));
  }

  [Fact]
  public void Map_Remove_RemovesAndReturnsValue()
  {
    var recorded = Run($"{RecordDeclaration}var m = new map<string, int> {{ \"a\": 10 }};\nvar v = m.remove(\"a\");\nrecord(v);\nrecord(m.has(\"a\"));");
    Assert.Equal(10L, ((IntValue)recorded[0]).Value);
    Assert.Equal(BoolValue.False, recorded[1]);
  }

  [Fact]
  public void Map_Remove_MissingKey_ThrowsRuntimeException()
  {
    Assert.Throws<RuntimeException>(() =>
      Run($"{RecordDeclaration}var m = new map<string, int> {{}};\nm.remove(\"x\");"));
  }

  [Fact]
  public void Map_Keys_ReturnsAllKeys()
  {
    var recorded = Run($"{RecordDeclaration}var m = new map<string, int> {{ \"a\": 1, \"b\": 2 }};\nvar k = m.keys();\nrecord(k.length);");
    Assert.Equal(2L, ((IntValue)Assert.Single(recorded)).Value);
  }

  [Fact]
  public void Map_Values_ReturnsAllValues()
  {
    var recorded = Run($"{RecordDeclaration}var m = new map<string, int> {{ \"a\": 1, \"b\": 2 }};\nvar v = m.values();\nrecord(v.length);");
    Assert.Equal(2L, ((IntValue)Assert.Single(recorded)).Value);
  }

  // ── typeof ───────────────────────────────────────────────────────────────────

  [Fact]
  public void Map_Typeof_ReturnsMapString()
  {
    var recorded = Run($"{RecordDeclaration}var m = new map<string, int> {{}};\nrecord(typeof m);");
    Assert.Equal("map", ((StringValue)Assert.Single(recorded)).Value);
  }

  // ── Enum keys ────────────────────────────────────────────────────────────────

  [Fact]
  public void Map_EnumKeys_WorkCorrectly()
  {
    var recorded = Run($"{RecordDeclaration}enum Color {{ Red, Green, Blue }}\nvar m = new map<Color, string> {{ Color.Red: \"red\", Color.Blue: \"blue\" }};\nrecord(m[Color.Red]);\nrecord(m.has(Color.Green));");
    Assert.Equal("red", ((StringValue)recorded[0]).Value);
    Assert.Equal(BoolValue.False, recorded[1]);
  }

  // ── Invalid key type ─────────────────────────────────────────────────────────

  [Fact]
  public void Map_InvalidKeyType_ThrowsRuntimeException()
  {
    Assert.Throws<RuntimeException>(() =>
      Run($"{RecordDeclaration}var m = new map<string, int> {{}};\nm.has(3.14);"));
  }
}
