using System.Collections.Generic;
using System.Linq;
using ALKScript.Interpreter.Common.Evaluation.Scheduling;

namespace ALKScript.Interpreter.Serialization
{
  /// <summary>JSON-friendly representation of a <see cref="PendingOperation"/> — the "which operation, with what arguments" half of an <see cref="OperationLogEntry"/>.</summary>
  public sealed class SerializedOperation
  {
    public string Name { get; set; } = "";

    public List<SerializedValue> Arguments { get; set; } = new();

    public static SerializedOperation FromOperation(PendingOperation operation) => new SerializedOperation
    {
      Name = operation.Name,
      Arguments = operation.Arguments.Select(SerializedValue.FromValue).ToList(),
    };

    public PendingOperation ToOperation() => new PendingOperation(Name, Arguments.Select(argument => argument.ToValue()).ToList());
  }

  /// <summary>JSON-friendly representation of an <see cref="OperationLogEntry"/> — see <see cref="OperationLogEntrySerializer"/>.</summary>
  public sealed class SerializedLogEntry
  {
    public SerializedOperation Operation { get; set; } = new();

    /// <summary>The serialized <see cref="OperationLogEntry.Result"/>, or <c>null</c> if the entry faulted.</summary>
    public SerializedValue? Result { get; set; }

    /// <summary>The <see cref="OperationLogEntry.FaultMessage"/>, or <c>null</c> if the entry succeeded.</summary>
    public string? FaultMessage { get; set; }
  }

  /// <summary>
  /// Converts <see cref="OperationLogEntry"/> instances to/from
  /// <see cref="SerializedLogEntry"/> DTOs. Restricts
  /// <see cref="OperationLogEntry.Result"/> and
  /// <see cref="PendingOperation.Arguments"/> to the primitive value set
  /// described by <see cref="SerializedValue"/> (the "Phase A" Capture/Restore
  /// design, docs/ASYNC_AWAIT_DESIGN.md Addendum 3) — <see cref="SerializedValue.FromValue"/>
  /// throws <see cref="System.NotSupportedException"/> for any other runtime
  /// value. <see cref="OperationLogEntry.FaultMessage"/> is always a plain
  /// string and never restricted.
  /// </summary>
  public static class OperationLogEntrySerializer
  {
    public static SerializedLogEntry Serialize(OperationLogEntry entry) => new SerializedLogEntry
    {
      Operation = SerializedOperation.FromOperation(entry.Operation),
      Result = entry.Result != null ? SerializedValue.FromValue(entry.Result) : null,
      FaultMessage = entry.FaultMessage,
    };

    public static OperationLogEntry Deserialize(SerializedLogEntry entry)
    {
      var operation = entry.Operation.ToOperation();

      return entry.FaultMessage != null
        ? OperationLogEntry.FromFault(operation, entry.FaultMessage)
        : OperationLogEntry.FromResult(operation, entry.Result!.ToValue());
    }
  }
}
