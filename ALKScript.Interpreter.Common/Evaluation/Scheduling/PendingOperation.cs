using System.Collections.Generic;
using ALKScript.Interpreter.Common.Evaluation.Values;

namespace ALKScript.Interpreter.Common.Evaluation.Scheduling
{
  /// <summary>
  /// A deliberately data-only descriptor of a not-yet-started <c>async
  /// native</c> operation: which one (<see cref="Name"/>, the declared
  /// operation's name) and with what already-evaluated <see cref="Arguments"/>.
  ///
  /// This is the shape that makes "lazy/deferred start" possible (see
  /// docs/ASYNC_AWAIT_DESIGN.md, "core requirements"): calling an <c>async
  /// native</c> function merely records *that* this operation was requested,
  /// with what arguments — it does not yet run the host-side effect. Whether,
  /// when, and how it actually starts is <see cref="IAsyncOperationBinder"/>'s
  /// concern (driven by <c>await</c> — "Suspend" — or end-of-script
  /// fire-and-forget — "Discard", decision #10), kept deliberately separate
  /// from this descriptor so the descriptor itself stays a small, inert,
  /// serializable value: exactly the unit a replay log persists as the
  /// "operation" half of each `(operation, result-or-fault)` pair (decision
  /// #17).
  /// </summary>
  public sealed class PendingOperation
  {
    /// <summary>The declared <c>async native</c> operation's name — what an <see cref="IAsyncOperationBinder"/> dispatches on to find the host-side effect to run.</summary>
    public string Name { get; }

    /// <summary>The already-evaluated argument values the operation was called with, in declaration order.</summary>
    public IReadOnlyList<ALKScriptValue> Arguments { get; }

    public PendingOperation(string name, IReadOnlyList<ALKScriptValue> arguments)
    {
      Name = name;
      Arguments = arguments;
    }
  }
}
