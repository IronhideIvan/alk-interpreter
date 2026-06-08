using System.Collections.Generic;
using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Common.Evaluation;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Evaluator;

namespace Tests.ALKScript.Interpreter.Evaluator.Unit;

public class FunctionValueFactoryTests
{
  private static FunctionDecl MakeDeclaration(string name, bool isNative) =>
    new FunctionDecl(
      isNative,
      isAsync: false,
      typeParameters: System.Array.Empty<string>(),
      returnType: Nodes.VoidType,
      name: Nodes.Identifier(name),
      parameters: System.Array.Empty<Parameter>(),
      body: isNative ? null : new BlockStmt(System.Array.Empty<Stmt>()));

  [Fact]
  public void Create_NonNativeDeclaration_ReturnsFunctionValueClosingOverEnvironment()
  {
    var factory = new FunctionValueFactory();
    var declaration = MakeDeclaration("greet", isNative: false);
    var closure = new ScriptEnvironment();

    var value = Assert.IsType<FunctionValue>(factory.Create(declaration, closure));

    Assert.Same(declaration, value.Declaration);
    Assert.Same(closure, value.Closure);
    Assert.Null(value.BoundInstance);
  }

  [Fact]
  public void Create_NativeDeclarationWithBinding_ReturnsNativeFunctionValue()
  {
    NativeFunctionImplementation implementation = _ => NullValue.Instance;
    var factory = new FunctionValueFactory(new Dictionary<string, NativeFunctionImplementation> { ["log"] = implementation });
    var declaration = MakeDeclaration("log", isNative: true);

    var value = Assert.IsType<NativeFunctionValue>(factory.Create(declaration, new ScriptEnvironment()));

    Assert.Equal("log", value.Name);
    Assert.Equal(0, value.Arity);
    Assert.Same(implementation, value.Implementation);
  }

  [Fact]
  public void Create_NativeDeclarationWithoutBinding_ThrowsRuntimeException()
  {
    var factory = new FunctionValueFactory();
    var declaration = MakeDeclaration("log", isNative: true);

    var exception = Assert.Throws<RuntimeException>(() => factory.Create(declaration, new ScriptEnvironment()));

    Assert.Contains("'log' has no host implementation registered", exception.Message);
  }

  [Fact]
  public void MethodAsFunctionDecl_AdaptsAMethodDeclarationsShape()
  {
    var name = Nodes.Identifier("speak");
    var parameters = new List<Parameter> { new Parameter(Nodes.VoidType, "volume") };
    var body = new BlockStmt(System.Array.Empty<Stmt>());
    var method = new MethodDecl(
      AccessModifier.Public,
      OverrideModifier.None,
      isNative: true,
      isAsync: true,
      typeParameters: new[] { "T" },
      returnType: Nodes.VoidType,
      name: name,
      parameters: parameters,
      body: body);

    var adapted = FunctionValueFactory.MethodAsFunctionDecl(method);

    Assert.Equal(method.IsNative, adapted.IsNative);
    Assert.Equal(method.IsAsync, adapted.IsAsync);
    Assert.Same(method.TypeParameters, adapted.TypeParameters);
    Assert.Same(method.ReturnType, adapted.ReturnType);
    Assert.Same(method.Name, adapted.Name);
    Assert.Same(method.Parameters, adapted.Parameters);
    Assert.Same(method.Body, adapted.Body);
  }
}
