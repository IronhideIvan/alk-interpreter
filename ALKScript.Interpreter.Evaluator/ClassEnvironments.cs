using ALKScript.Interpreter.Common.Evaluation;
using ALKScript.Interpreter.Common.Evaluation.Values;

namespace ALKScript.Interpreter.Evaluator
{
  /// <summary>
  /// Resolves the environment methods/constructors of a class close over.
  /// </summary>
  internal static class ClassEnvironments
  {
    /// <remarks>
    /// A full implementation would capture the environment the class was
    /// declared in; until that is threaded through <see cref="ClassValue"/>,
    /// an empty top-level scope stands in.
    /// </remarks>
    public static ScriptEnvironment For(ClassValue classValue) => new ScriptEnvironment();
  }
}
