using System;
using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Parser.Modules
{
  /// <summary>
  /// Thrown by <see cref="ProgramLoader"/> when a module graph cannot be
  /// assembled: an import specifier cannot be resolved, two modules import
  /// each other (directly or transitively), or a named import refers to a
  /// declaration the target module does not export. These are compile-time
  /// errors per §9.2 of the language spec.
  /// </summary>
  public class ModuleLoadException : Exception
  {
    /// <summary>The token (import specifier string or imported name) the error is reported against.</summary>
    public ALKScriptToken Token { get; }

    public ModuleLoadException(ALKScriptToken token, string message)
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
