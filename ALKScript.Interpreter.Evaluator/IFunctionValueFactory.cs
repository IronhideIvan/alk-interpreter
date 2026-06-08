using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Common.Evaluation;
using ALKScript.Interpreter.Common.Evaluation.Values;

namespace ALKScript.Interpreter.Evaluator
{
  /// <summary>
  /// Builds the callable <see cref="ALKScriptValue"/> for a function or method
  /// declaration, resolving <c>native</c> declarations against host bindings.
  /// </summary>
  public interface IFunctionValueFactory
  {
    /// <summary>
    /// Creates the callable value for <paramref name="declaration"/> closing
    /// over <paramref name="closure"/>. A <c>native</c> declaration with no
    /// matching host binding fails with a <see cref="RuntimeException"/>.
    /// </summary>
    ALKScriptValue Create(FunctionDecl declaration, ScriptEnvironment closure);
  }
}
