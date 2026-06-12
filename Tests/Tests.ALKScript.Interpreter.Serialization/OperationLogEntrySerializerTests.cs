using System.Collections.Generic;
using ALKScript.Interpreter.Common.Evaluation.Scheduling;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Serialization;

namespace Tests.ALKScript.Interpreter.Serialization;

/// <summary>
/// Step 12 coverage for the cursor-rewrite plan (docs:
/// validated-nibbling-narwhal): <see cref="OperationLogEntrySerializer"/>
/// round-trips of successful and faulted <see cref="OperationLogEntry"/>
/// entries (the "Phase A" Capture/Restore design, docs/ASYNC_AWAIT_DESIGN.md
/// Addendum 3).
/// </summary>
public class OperationLogEntrySerializerTests
{
  [Fact]
  public void Serialize_SuccessfulEntry_RoundTrips()
  {
    var operation = new PendingOperation("fetch", new ALKScriptValue[] { new IntValue(1), new StringValue("a") });
    var entry = OperationLogEntry.FromResult(operation, new IntValue(99));

    var serialized = OperationLogEntrySerializer.Serialize(entry);
    var restored = OperationLogEntrySerializer.Deserialize(serialized);

    Assert.Equal("fetch", restored.Operation.Name);
    Assert.Equal(1L, Assert.IsType<IntValue>(restored.Operation.Arguments[0]).Value);
    Assert.Equal("a", Assert.IsType<StringValue>(restored.Operation.Arguments[1]).Value);
    Assert.False(restored.IsFaulted);
    Assert.Equal(99L, Assert.IsType<IntValue>(restored.Result!).Value);
  }

  [Fact]
  public void Serialize_FaultedEntry_RoundTrips()
  {
    var operation = new PendingOperation("fetch", new List<ALKScriptValue>());
    var entry = OperationLogEntry.FromFault(operation, "boom");

    var serialized = OperationLogEntrySerializer.Serialize(entry);
    var restored = OperationLogEntrySerializer.Deserialize(serialized);

    Assert.Equal("fetch", restored.Operation.Name);
    Assert.True(restored.IsFaulted);
    Assert.Equal("boom", restored.FaultMessage);
    Assert.Null(restored.Result);
  }
}
