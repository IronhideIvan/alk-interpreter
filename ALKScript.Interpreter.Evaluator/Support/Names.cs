using ALKScript.Interpreter.Common.Evaluation;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Evaluator
{
  /// <summary>Resolves identifiers against an <see cref="ScriptEnvironment"/>.</summary>
  internal static class Names
  {
    public static ALKScriptValue LookUp(ALKScriptToken name, ScriptEnvironment environment)
    {
      if (environment.TryGet(name.Lexeme, out var value))
      {
        return value;
      }

      throw new RuntimeException(name, $"Undefined name '{name.Lexeme}'.");
    }
  }
}
