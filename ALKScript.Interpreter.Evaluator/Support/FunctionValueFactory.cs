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
    private readonly ScriptNativeMethodBindings _nativeMethodBindings;

    /// <summary>
    /// <paramref name="nativeBindings"/> supplies the host implementations for
    /// free-standing <c>native function</c> declarations, keyed by declared
    /// name. <paramref name="nativeMethodBindings"/> supplies the host
    /// implementations for <c>native</c> methods, keyed by declaring class and
    /// member name — see <see cref="ScriptNativeMethodBindings"/> for why
    /// methods need a separate, class-scoped table.
    /// </summary>
    public FunctionValueFactory(ScriptNativeBindings? nativeBindings = null, ScriptNativeMethodBindings? nativeMethodBindings = null)
    {
      _nativeBindings = nativeBindings ?? new ScriptNativeBindings();
      _nativeMethodBindings = nativeMethodBindings ?? new ScriptNativeMethodBindings();
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

    public ALKScriptValue CreateMethod(MethodDecl declaration, ClassValue declaringClass, ScriptEnvironment closure, InstanceValue? boundInstance)
    {
      if (!declaration.IsNative)
      {
        return new FunctionValue(MethodAsFunctionDecl(declaration), closure, boundInstance);
      }

      string className = declaringClass.Declaration.Name.Lexeme;
      string memberName = declaration.Name.Lexeme;

      if (boundInstance == null)
      {
        throw new RuntimeException(declaration.Name, $"Native method '{className}.{memberName}' must be accessed on an instance.");
      }

      if (_nativeMethodBindings.TryGetValue(className, memberName, out var implementation))
      {
        var instance = boundInstance;
        return new NativeFunctionValue(memberName, declaration.Parameters.Count, arguments => implementation(instance, arguments));
      }

      throw new RuntimeException(declaration.Name, $"Native method '{className}.{memberName}' has no host implementation registered.");
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
