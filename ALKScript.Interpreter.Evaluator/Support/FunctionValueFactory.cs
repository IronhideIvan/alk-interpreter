using System.Collections.Generic;
using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Common.Evaluation;
using ALKScript.Interpreter.Common.Evaluation.Values;

namespace ALKScript.Interpreter.Evaluator
{
  /// <inheritdoc cref="IFunctionValueFactory"/>
  public class FunctionValueFactory : IFunctionValueFactory
  {
    private readonly ScriptNativeBindings _nativeBindings;

    /// <summary>
    /// <paramref name="nativeBindings"/> supplies the host implementations for
    /// <c>native</c> function/method declarations, keyed by declared name.
    /// </summary>
    public FunctionValueFactory(ScriptNativeBindings? nativeBindings = null)
    {
      _nativeBindings = nativeBindings ?? new ScriptNativeBindings();
    }

    public ALKScriptValue Create(FunctionDecl declaration, ScriptEnvironment closure)
    {
      if (!declaration.IsNative)
      {
        return new FunctionValue(declaration, closure);
      }

      if (_nativeBindings.TryGetValue(declaration.Name.Lexeme, out var implementation))
      {
        return new NativeFunctionValue(declaration.Name.Lexeme, declaration.Parameters.Count, implementation);
      }

      throw new RuntimeException(declaration.Name, $"Native function '{declaration.Name.Lexeme}' has no host implementation registered.");
    }

    /// <summary>
    /// Methods and functions share evaluation logic but not an AST type;
    /// this adapts a <see cref="MethodDecl"/> to the <see cref="FunctionDecl"/>
    /// shape <see cref="FunctionValue"/> expects.
    /// </summary>
    public static FunctionDecl MethodAsFunctionDecl(MethodDecl method)
    {
      return new FunctionDecl(
        method.IsNative,
        method.IsAsync,
        method.TypeParameters,
        method.ReturnType,
        method.Name,
        method.Parameters,
        method.Body);
    }
  }
}
