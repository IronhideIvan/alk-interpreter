using System.Collections.Generic;
using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Common.Evaluation;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Common.Token;
using ALKScript.Interpreter.Evaluator;

namespace Tests.ALKScript.Interpreter.Evaluator.Unit;

public class CallInvokerTests
{
  private static readonly ALKScriptToken Site = Nodes.Token(ALKScriptTokenType.RightParen, ")");

  private static FunctionDecl MakeFunctionDeclaration(IReadOnlyList<Parameter> parameters) =>
    new FunctionDecl(false, false, System.Array.Empty<string>(), Nodes.VoidType, Nodes.Identifier("f"), parameters, new BlockStmt(System.Array.Empty<Stmt>()));

  private static ClassValue MakeClass(IReadOnlyList<MemberDecl> members, string name = "Foo") =>
    new ClassValue(new ClassDecl(false, Nodes.Identifier(name), System.Array.Empty<string>(), null, System.Array.Empty<TypeNode>(), members), null, new ScriptEnvironment());

  [Fact]
  public void Call_WithClassValue_DelegatesToConstruct()
  {
    var classValue = MakeClass(System.Array.Empty<MemberDecl>());
    var context = new FakeEvaluationContext();
    context.ExecuteBlockImpl = (_, _) => { };

    var value = new CallInvoker(context).Call(classValue, System.Array.Empty<ALKScriptValue>(), Site);

    Assert.IsType<InstanceValue>(value);
  }

  [Fact]
  public void Call_NativeFunctionWithMatchingArity_InvokesItsImplementation()
  {
    IReadOnlyList<ALKScriptValue>? receivedArguments = null;
    var native = new NativeFunctionValue("f", 1, arguments =>
    {
      receivedArguments = arguments;
      return new IntValue(42);
    });
    var context = new FakeEvaluationContext();

    var value = new CallInvoker(context).Call(native, new ALKScriptValue[] { new IntValue(1) }, Site);

    Assert.Equal(42L, Assert.IsType<IntValue>(value).Value);
    Assert.Single(receivedArguments!);
  }

  [Fact]
  public void Call_WithMismatchedArity_ThrowsRuntimeException()
  {
    var native = new NativeFunctionValue("f", 2, _ => NullValue.Instance);
    var context = new FakeEvaluationContext();

    var exception = Assert.Throws<RuntimeException>(() => new CallInvoker(context).Call(native, new ALKScriptValue[] { new IntValue(1) }, Site));

    Assert.Contains("Expected 2 argument(s) but got 1", exception.Message);
  }

  [Fact]
  public void Call_NonCallableValue_ThrowsRuntimeException()
  {
    var context = new FakeEvaluationContext();

    var exception = Assert.Throws<RuntimeException>(() => new CallInvoker(context).Call(new IntValue(1), System.Array.Empty<ALKScriptValue>(), Site));

    Assert.Contains("Cannot call a value of type 'int'", exception.Message);
  }

  [Fact]
  public void Call_FunctionValue_RunsItsBodyAndConsumesAReturnSignal()
  {
    var declaration = MakeFunctionDeclaration(new[] { new Parameter(Nodes.VoidType, "x") });
    var closure = new ScriptEnvironment();
    var function = new FunctionValue(declaration, closure);

    ScriptEnvironment? capturedCallEnvironment = null;
    var context = new FakeEvaluationContext();
    context.ExecuteBlockImpl = (_, environment) =>
    {
      capturedCallEnvironment = environment;
      context.Signal = Signal.Return(new IntValue(7));
    };

    var value = new CallInvoker(context).Call(function, new ALKScriptValue[] { new IntValue(3) }, Site);

    Assert.Equal(7L, Assert.IsType<IntValue>(value).Value);
    Assert.Null(context.Signal);
    Assert.True(capturedCallEnvironment!.TryGet("x", out var bound));
    Assert.Equal(3L, Assert.IsType<IntValue>(bound).Value);
  }

  [Fact]
  public void Call_FunctionValueWithBoundInstance_DefinesThisInTheCallEnvironment()
  {
    var declaration = MakeFunctionDeclaration(System.Array.Empty<Parameter>());
    var instance = new InstanceValue(MakeClass(System.Array.Empty<MemberDecl>()));
    var function = new FunctionValue(declaration, new ScriptEnvironment(), instance);

    ScriptEnvironment? capturedCallEnvironment = null;
    var context = new FakeEvaluationContext();
    context.ExecuteBlockImpl = (_, environment) => capturedCallEnvironment = environment;

    new CallInvoker(context).Call(function, System.Array.Empty<ALKScriptValue>(), Site);

    Assert.True(capturedCallEnvironment!.TryGet("this", out var boundThis));
    Assert.Same(instance, boundThis);
  }

  [Fact]
  public void Call_FunctionValue_LeavesAThrownSignalPendingForTheCaller()
  {
    var declaration = MakeFunctionDeclaration(System.Array.Empty<Parameter>());
    var function = new FunctionValue(declaration, new ScriptEnvironment());

    var context = new FakeEvaluationContext();
    context.ExecuteBlockImpl = (_, _) => context.Signal = Signal.Thrown(new StringValue("boom"));

    new CallInvoker(context).Call(function, System.Array.Empty<ALKScriptValue>(), Site);

    Assert.NotNull(context.Signal);
    Assert.Equal(SignalKind.Thrown, context.Signal!.Value.Kind);
  }

  [Fact]
  public void Construct_WithMatchingConstructor_BindsParametersAndThis()
  {
    var parameters = new List<Parameter> { new Parameter(Nodes.VoidType, "name") };
    var body = new BlockStmt(System.Array.Empty<Stmt>());
    var constructor = new ConstructorDecl(AccessModifier.Public, parameters, body);
    var classValue = MakeClass(new MemberDecl[] { constructor });

    ScriptEnvironment? capturedConstructorEnvironment = null;
    var context = new FakeEvaluationContext();
    context.ExecuteBlockImpl = (_, environment) => capturedConstructorEnvironment = environment;

    var value = new CallInvoker(context).Construct(classValue, new ALKScriptValue[] { new StringValue("Ada") }, Site);

    var instance = Assert.IsType<InstanceValue>(value);
    Assert.True(capturedConstructorEnvironment!.TryGet("this", out var boundThis));
    Assert.Same(instance, boundThis);
    Assert.True(capturedConstructorEnvironment.TryGet("name", out var boundName));
    Assert.Equal("Ada", Assert.IsType<StringValue>(boundName).Value);
  }

  [Fact]
  public void Construct_WithMismatchedConstructorArity_ThrowsRuntimeException()
  {
    var constructor = new ConstructorDecl(AccessModifier.Public, new[] { new Parameter(Nodes.VoidType, "x") }, new BlockStmt(System.Array.Empty<Stmt>()));
    var classValue = MakeClass(new MemberDecl[] { constructor });
    var context = new FakeEvaluationContext();

    var exception = Assert.Throws<RuntimeException>(() => new CallInvoker(context).Construct(classValue, System.Array.Empty<ALKScriptValue>(), Site));

    Assert.Contains("Expected 1 argument(s) but got 0", exception.Message);
  }

  [Fact]
  public void Construct_WithoutAConstructor_ReturnsAnEmptyInstanceWhenCalledWithNoArguments()
  {
    var classValue = MakeClass(System.Array.Empty<MemberDecl>());
    var context = new FakeEvaluationContext();

    var value = new CallInvoker(context).Construct(classValue, System.Array.Empty<ALKScriptValue>(), Site);

    Assert.IsType<InstanceValue>(value);
  }

  [Fact]
  public void Construct_WithoutAConstructorButGivenArguments_ThrowsRuntimeException()
  {
    var classValue = MakeClass(System.Array.Empty<MemberDecl>());
    var context = new FakeEvaluationContext();

    var exception = Assert.Throws<RuntimeException>(() => new CallInvoker(context).Construct(classValue, new ALKScriptValue[] { new IntValue(1) }, Site));

    Assert.Contains("Expected 0 argument(s) but got 1", exception.Message);
  }

  [Fact]
  public void Construct_ConsumesABareReturnSignalFromTheConstructorBody()
  {
    var constructor = new ConstructorDecl(AccessModifier.Public, System.Array.Empty<Parameter>(), new BlockStmt(System.Array.Empty<Stmt>()));
    var classValue = MakeClass(new MemberDecl[] { constructor });

    var context = new FakeEvaluationContext();
    context.ExecuteBlockImpl = (_, _) => context.Signal = Signal.Return(NullValue.Instance);

    new CallInvoker(context).Construct(classValue, System.Array.Empty<ALKScriptValue>(), Site);

    Assert.Null(context.Signal);
  }
}
