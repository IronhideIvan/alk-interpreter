using ALKScript.Interpreter.Common;
using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Evaluator
{
  /// <summary>
  /// Thrown when evaluation encounters a condition the language considers an
  /// error (e.g. an undefined name, a type mismatch, calling a non-callable
  /// value). Mirrors <c>ParseException</c>'s shape: a source token plus a
  /// formatted, location-bearing message.
  /// </summary>
  public class RuntimeException : InterpreterBaseException
  {
    public ALKScriptToken Token { get; }

    public RuntimeException(ALKScriptToken token, string message)
      : base(FormatMessage(token, message))
    {
      Token = token;
    }

    private static string FormatMessage(ALKScriptToken token, string message)
    {
      return $"[line {token.Line}, col {token.Column}] Error at '{token.Lexeme}': {message}";
    }
  }
}
