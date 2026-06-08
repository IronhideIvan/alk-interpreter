namespace ALKScript.Interpreter.Evaluator
{
  /// <summary>The kind of non-local exit a <see cref="Signal"/> represents.</summary>
  internal enum SignalKind
  {
    /// <summary>Unwinding to the nearest enclosing function/constructor call ("return").</summary>
    Return,

    /// <summary>Unwinding to the nearest enclosing "try"/"catch", or the top level ("throw").</summary>
    Thrown
  }
}
