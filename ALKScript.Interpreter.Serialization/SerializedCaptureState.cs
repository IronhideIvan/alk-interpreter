using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ALKScript.Interpreter.Common.Evaluation.Scheduling;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Common.Modules;
using ALKScript.Interpreter.Evaluator.Cursor;

namespace ALKScript.Interpreter.Serialization
{
  /// <summary>JSON-friendly representation of a <see cref="CursorCaptureState"/> — see <see cref="CursorStateSerializer"/>.</summary>
  public sealed class SerializedCaptureState
  {
    public int Phase { get; set; }

    public int ModuleIndex { get; set; }

    public List<SerializedLogEntry> Log { get; set; } = new();
  }

  /// <summary>
  /// The public entry point for capturing a suspended
  /// <see cref="CursorProgramEvaluator"/> run to a JSON byte stream, and
  /// restoring one from it (the "Phase A" Capture/Restore design,
  /// docs/ASYNC_AWAIT_DESIGN.md Addendum 3). A host that wants a different
  /// wire format can instead call <see cref="CursorProgramEvaluator.Capture"/>/
  /// <see cref="CursorProgramEvaluator.Restore"/> directly and write its own
  /// serializer against <see cref="CursorCaptureState"/>/<see cref="OperationLogEntry"/>.
  /// </summary>
  public static class CursorStateSerializer
  {
    /// <summary>
    /// Captures <paramref name="evaluator"/>'s suspended state (see
    /// <see cref="CursorProgramEvaluator.Capture"/> — throws if the evaluator
    /// is not currently suspended) and serializes it to JSON bytes.
    /// </summary>
    public static byte[] Capture(CursorProgramEvaluator evaluator)
    {
      var state = evaluator.Capture();
      var serialized = new SerializedCaptureState
      {
        Phase = state.Phase,
        ModuleIndex = state.ModuleIndex,
        Log = state.Log.Select(OperationLogEntrySerializer.Serialize).ToList(),
      };

      return JsonSerializer.SerializeToUtf8Bytes(serialized);
    }

    /// <summary>
    /// Deserializes <paramref name="data"/> (as produced by <see cref="Capture"/>)
    /// and reconstructs a suspended run via <see cref="CursorProgramEvaluator.Restore"/>.
    /// <paramref name="graph"/> must be an equivalent module graph to the one
    /// the original run was evaluating (rebuilt from the same source files/
    /// module identifiers).
    /// </summary>
    public static CursorProgramEvaluator Restore(
      ModuleGraph graph,
      byte[] data,
      out ProgramRunResult result,
      ScriptNativeBindings? nativeBindings = null,
      ScriptNativeMethodBindings? nativeMethodBindings = null,
      IAsyncOperationBinder? operationBinder = null)
    {
      var serialized = JsonSerializer.Deserialize<SerializedCaptureState>(data)
        ?? throw new System.FormatException("Captured state JSON deserialized to null.");

      var state = new CursorCaptureState(
        serialized.Phase,
        serialized.ModuleIndex,
        serialized.Log.Select(OperationLogEntrySerializer.Deserialize).ToList());

      return CursorProgramEvaluator.Restore(graph, state, out result, nativeBindings, nativeMethodBindings, operationBinder);
    }
  }
}
