using System;

namespace ALKScript.Interpreter.Common
{
  /// <summary>
  /// Common base for all exceptions thrown by the interpreter (lexing,
  /// parsing, module loading, evaluation, etc.).
  /// </summary>
  public abstract class InterpreterBaseException : Exception
  {
    protected InterpreterBaseException(string message)
      : base(message)
    {
    }

    protected InterpreterBaseException(string message, Exception innerException)
      : base(message, innerException)
    {
    }
  }
}
