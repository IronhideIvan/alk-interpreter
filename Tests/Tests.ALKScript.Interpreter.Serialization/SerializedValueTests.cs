using System;
using System.Collections.Generic;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Serialization;

namespace Tests.ALKScript.Interpreter.Serialization;

/// <summary>
/// Step 12 coverage for the cursor-rewrite plan (docs:
/// validated-nibbling-narwhal): <see cref="SerializedValue"/>'s primitive
/// round-trip and the "Phase A" non-primitive restriction
/// (docs/ASYNC_AWAIT_DESIGN.md Addendum 3).
/// </summary>
public class SerializedValueTests
{
  [Fact]
  public void FromValue_Int_RoundTrips()
  {
    var serialized = SerializedValue.FromValue(new IntValue(42));
    var value = Assert.IsType<IntValue>(serialized.ToValue());

    Assert.Equal(42L, value.Value);
  }

  [Fact]
  public void FromValue_Float_RoundTrips()
  {
    var serialized = SerializedValue.FromValue(new FloatValue(1.5));
    var value = Assert.IsType<FloatValue>(serialized.ToValue());

    Assert.Equal(1.5, value.Value);
  }

  [Fact]
  public void FromValue_String_RoundTrips()
  {
    var serialized = SerializedValue.FromValue(new StringValue("hello"));
    var value = Assert.IsType<StringValue>(serialized.ToValue());

    Assert.Equal("hello", value.Value);
  }

  [Fact]
  public void FromValue_Bool_RoundTrips()
  {
    var serialized = SerializedValue.FromValue(BoolValue.Of(true));
    var value = Assert.IsType<BoolValue>(serialized.ToValue());

    Assert.True(value.Value);
  }

  [Fact]
  public void FromValue_Null_RoundTrips()
  {
    var serialized = SerializedValue.FromValue(NullValue.Instance);

    Assert.IsType<NullValue>(serialized.ToValue());
  }

  [Fact]
  public void FromValue_ArrayOfPrimitives_RoundTrips()
  {
    var array = new ArrayValue(new List<ALKScriptValue> { new IntValue(1), new StringValue("two"), BoolValue.Of(false) });

    var serialized = SerializedValue.FromValue(array);
    var value = Assert.IsType<ArrayValue>(serialized.ToValue());

    Assert.Equal(1L, Assert.IsType<IntValue>(value.Items[0]).Value);
    Assert.Equal("two", Assert.IsType<StringValue>(value.Items[1]).Value);
    Assert.False(Assert.IsType<BoolValue>(value.Items[2]).Value);
  }

  [Fact]
  public void FromValue_NonPrimitiveValue_Throws()
  {
    var enumValue = new EnumValue("Color", "Red", 0);

    var exception = Assert.Throws<NotSupportedException>(() => SerializedValue.FromValue(enumValue));
    Assert.Contains("EnumValue", exception.Message);
  }
}
