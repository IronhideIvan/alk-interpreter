using System.Collections.Generic;

namespace ALKScript.Interpreter.Evaluator.Cursor
{
  /// <summary>
  /// A snapshot of a <c>ClassValue</c>'s <c>StaticFields</c> — shared mutable
  /// state not owned by any single instance, captured separately from
  /// <see cref="CapturedHeapEntry"/> and grafted back onto the
  /// <c>ClassValue</c> that <see cref="CursorProgramEvaluator.RestoreStructural"/>'s
  /// declaration-prefix run re-creates (docs/ASYNC_AWAIT_DESIGN.md Addendum 3,
  /// Step 10).
  /// </summary>
  public sealed class CapturedClassStaticFields
  {
    public AstReference ClassRef { get; }

    public Dictionary<string, CapturedHeapValue> Fields { get; }

    public CapturedClassStaticFields(AstReference classRef, Dictionary<string, CapturedHeapValue> fields)
    {
      ClassRef = classRef;
      Fields = fields;
    }
  }
}
