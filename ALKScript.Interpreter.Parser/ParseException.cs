using System;
using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Parser
{
  /// <summary>
  /// Thrown when the parser encounters a token sequence that does not conform
  /// to the language grammar.
  /// </summary>
  public class ParseException : Exception
  {
    public ALKScriptToken Token { get; }

    public ParseException(ALKScriptToken token, string message)
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
