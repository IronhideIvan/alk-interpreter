using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Common.Evaluation;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Evaluator
{
  /// <summary>
  /// Enforces non-nullable type annotations: a type without a trailing "?"
  /// rejects "null" wherever a value of that type is assigned, passed, or
  /// returned. Untyped slots ("var" locals, generic type parameters with no
  /// annotation) are unconstrained.
  /// </summary>
  internal static class Nullability
  {
    public static void EnsureAssignable(TypeNode? type, ALKScriptValue value, ALKScriptToken site, string description)
    {
      if (type == null || type.IsNullable)
      {
        return;
      }

      if (value is NullValue)
      {
        throw new RuntimeException(site, $"Cannot assign 'null' to {description} of non-nullable type '{type}'.");
      }
    }
  }
}
