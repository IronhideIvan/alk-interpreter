using System.Collections.Generic;
using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Common.Evaluation.Scheduling;
using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Evaluator.Cursor
{
  /// <summary>
  /// A captured <see cref="AwaitHandle"/> suspension — part of the "Phase B"
  /// structural Capture/Restore design (docs/ASYNC_AWAIT_DESIGN.md Addendum 3,
  /// Steps 7 and 12). For a single-element <c>await</c>, <see cref="Operation"/>
  /// is non-null and <see cref="CompositeElements"/> is null —
  /// <see cref="CursorProgramEvaluator.RestoreStructural"/> reissues
  /// <see cref="Operation"/> via <c>IAsyncOperationBinder.Start</c>, restarting
  /// the host-side effect from scratch (the in-flight operation's own progress
  /// is the host's concern, not part of this snapshot). For a composite
  /// <c>await [a, b, c]</c>, <see cref="CompositeElements"/> is non-null and
  /// <see cref="Operation"/>/<see cref="ElementType"/> are both null — each
  /// live (not-yet-settled) element is reissued the same way.
  /// </summary>
  public sealed class CapturedPendingAwait
  {
    public PendingOperation? Operation { get; set; }

    public TypeNode? ElementType { get; set; }

    /// <summary>The <c>await</c> keyword token (see <see cref="AwaitHandle.Site"/>) — serializable directly, see <see cref="ALKScriptToken"/>.</summary>
    public ALKScriptToken Site { get; set; } = null!;

    /// <summary>Set only for a composite <c>await [a, b, c]</c> suspension; <c>null</c> for a single-element <c>await</c>.</summary>
    public List<CapturedAwaitElement>? CompositeElements { get; set; }
  }
}
