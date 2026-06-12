using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ALKScript.Interpreter.Evaluator.Cursor
{
  /// <summary>
  /// Reference-identity equality comparer (netstandard2.0 doesn't have the
  /// built-in <c>System.Collections.Generic.ReferenceEqualityComparer</c>,
  /// added in .NET 5). Used by the "Phase B" structural Capture/Restore design
  /// (docs/ASYNC_AWAIT_DESIGN.md Addendum 3) wherever AST nodes or
  /// <c>ScriptEnvironment</c>s are keyed by reference identity.
  /// </summary>
  internal sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T> where T : class
  {
    public static readonly ReferenceEqualityComparer<T> Instance = new ReferenceEqualityComparer<T>();

    public bool Equals(T? x, T? y) => ReferenceEquals(x, y);

    public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
  }
}
