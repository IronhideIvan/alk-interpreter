using ALKScript.Interpreter.Common.Evaluation;
using ALKScript.Interpreter.Common.Evaluation.Values;

namespace ALKScript.Interpreter.Evaluator
{
  /// <summary>
  /// Resolves the environment methods/constructors of a class close over:
  /// the environment the class was declared in, so its members can see
  /// enclosing module-level bindings (other top-level declarations, imports,
  /// etc.) — the same way a function's body closes over its declaring scope.
  /// </summary>
  internal static class ClassEnvironments
  {
    public static ScriptEnvironment For(ClassValue classValue) => classValue.Closure;
  }
}
