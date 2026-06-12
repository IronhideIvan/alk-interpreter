using System;
using System.Collections.Generic;
using System.Linq;
using AlkValues = ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScriptValue = ALKScript.Interpreter.Common.Evaluation.Values.ALKScriptValue;

namespace ALKScript.Interpreter.Serialization
{
  /// <summary>
  /// JSON-friendly representation of an <see cref="ALKScriptValue"/>, restricted
  /// (for the "Phase A" Capture/Restore design, docs/ASYNC_AWAIT_DESIGN.md
  /// Addendum 3) to int/float/string/bool/null values and arrays of those
  /// (recursively). Any other runtime value (object instances, functions,
  /// thunks, pending operations, classes, etc.) cannot cross the
  /// Capture/Restore boundary yet — reconstructing those requires the
  /// AST-reference-based "Phase B" design.
  /// </summary>
  public sealed class SerializedValue
  {
    public string Type { get; set; } = "";

    public long? IntValue { get; set; }

    public double? FloatValue { get; set; }

    public string? StringValue { get; set; }

    public bool? BoolValue { get; set; }

    public List<SerializedValue>? Items { get; set; }

    public static SerializedValue FromValue(ALKScriptValue value)
    {
      switch (value)
      {
        case AlkValues.IntValue intValue:
          return new SerializedValue { Type = "int", IntValue = intValue.Value };

        case AlkValues.FloatValue floatValue:
          return new SerializedValue { Type = "float", FloatValue = floatValue.Value };

        case AlkValues.StringValue stringValue:
          return new SerializedValue { Type = "string", StringValue = stringValue.Value };

        case AlkValues.BoolValue boolValue:
          return new SerializedValue { Type = "bool", BoolValue = boolValue.Value };

        case AlkValues.NullValue:
          return new SerializedValue { Type = "null" };

        case AlkValues.ArrayValue arrayValue:
          return new SerializedValue
          {
            Type = "array",
            Items = arrayValue.Items.Select(FromValue).ToList(),
          };

        default:
          throw new NotSupportedException(
            $"Cannot capture a value of runtime type '{value.GetType().Name}' (ALKScript type '{value.TypeName}') — " +
            "only int/float/string/bool/null/array values can cross the Capture/Restore boundary " +
            "in the current ('Phase A', replay-based) design. Reconstructing object/function/thunk " +
            "values requires the 'Phase B' (structural snapshot) design — see docs/ASYNC_AWAIT_DESIGN.md Addendum 3.");
      }
    }

    public ALKScriptValue ToValue()
    {
      switch (Type)
      {
        case "int":
          return new AlkValues.IntValue(IntValue ?? throw new FormatException("Serialized 'int' value is missing IntValue."));

        case "float":
          return new AlkValues.FloatValue(FloatValue ?? throw new FormatException("Serialized 'float' value is missing FloatValue."));

        case "string":
          return new AlkValues.StringValue(StringValue ?? throw new FormatException("Serialized 'string' value is missing StringValue."));

        case "bool":
          return AlkValues.BoolValue.Of(BoolValue ?? throw new FormatException("Serialized 'bool' value is missing BoolValue."));

        case "null":
          return AlkValues.NullValue.Instance;

        case "array":
          var items = Items ?? throw new FormatException("Serialized 'array' value is missing Items.");
          return new AlkValues.ArrayValue(items.Select(item => item.ToValue()).ToList());

        default:
          throw new FormatException($"Unknown serialized value type '{Type}'.");
      }
    }
  }
}
