using System.Collections.Generic;
using System.Linq;
using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Common.Evaluation;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Common.Token;
using ALKScript.Interpreter.Evaluator;

namespace Tests.ALKScript.Interpreter.Evaluator.Unit;

public class ExpressionEvaluatorTests
{
  private static ExpressionEvaluator MakeEvaluator(FakeEvaluationContext context, IFunctionValueFactory? functionValueFactory = null) =>
    new ExpressionEvaluator(context, functionValueFactory ?? new FunctionValueFactory());

  [Theory]
  [InlineData(null)]
  [InlineData(true)]
  [InlineData(1L)]
  [InlineData(1.5)]
  [InlineData("text")]
  public void Eval_Literal_ProducesTheMatchingValueType(object? literalValue)
  {
    var context = new FakeEvaluationContext();
    var value = MakeEvaluator(context).Eval(Nodes.Literal(literalValue), new ScriptEnvironment());

    switch (literalValue)
    {
      case null: Assert.Same(NullValue.Instance, value); break;
      case bool b: Assert.Equal(b, Assert.IsType<BoolValue>(value).Value); break;
      case long l: Assert.Equal(l, Assert.IsType<IntValue>(value).Value); break;
      case double d: Assert.Equal(d, Assert.IsType<FloatValue>(value).Value); break;
      case string s: Assert.Equal(s, Assert.IsType<StringValue>(value).Value); break;
    }
  }

  [Fact]
  public void Eval_Identifier_ResolvesAgainstTheEnvironment()
  {
    var context = new FakeEvaluationContext();
    var environment = new ScriptEnvironment();
    environment.Define("x", new IntValue(9));

    var value = MakeEvaluator(context).Eval(Nodes.Ident("x"), environment);

    Assert.Equal(9L, Assert.IsType<IntValue>(value).Value);
  }

  [Fact]
  public void Eval_UndefinedIdentifier_ThrowsRuntimeException()
  {
    var context = new FakeEvaluationContext();

    var exception = Assert.Throws<RuntimeException>(() => MakeEvaluator(context).Eval(Nodes.Ident("missing"), new ScriptEnvironment()));

    Assert.Contains("Undefined name 'missing'", exception.Message);
  }

  [Fact]
  public void Eval_Grouping_DelegatesToTheInnerExpression()
  {
    var context = new FakeEvaluationContext();

    var value = MakeEvaluator(context).Eval(new GroupingExpr(Nodes.Literal(3L)), new ScriptEnvironment());

    Assert.Equal(3L, Assert.IsType<IntValue>(value).Value);
  }

  [Fact]
  public void Eval_ArrayLiteral_EvaluatesEachElementInOrder()
  {
    var context = new FakeEvaluationContext();
    var elements = new List<Expr> { Nodes.Literal(1L), Nodes.Literal(2L), Nodes.Literal(3L) };

    var value = Assert.IsType<ArrayValue>(MakeEvaluator(context).Eval(new ArrayLiteralExpr(elements), new ScriptEnvironment()));

    Assert.Equal(new long[] { 1, 2, 3 }, value.Items.ConvertAll(item => ((IntValue)item).Value));
  }

  [Fact]
  public void Eval_AssignmentToIdentifier_UpdatesTheBindingAndReturnsTheValue()
  {
    var context = new FakeEvaluationContext();
    var environment = new ScriptEnvironment();
    environment.Define("x", new IntValue(1));
    var assignment = new AssignmentExpr(Nodes.Ident("x"), Nodes.Literal(2L));

    var value = MakeEvaluator(context).Eval(assignment, environment);

    Assert.Equal(2L, Assert.IsType<IntValue>(value).Value);
    Assert.True(environment.TryGet("x", out var stored));
    Assert.Equal(2L, Assert.IsType<IntValue>(stored).Value);
  }

  [Fact]
  public void Eval_AssignmentToUndefinedIdentifier_ThrowsRuntimeException()
  {
    var context = new FakeEvaluationContext();
    var assignment = new AssignmentExpr(Nodes.Ident("missing"), Nodes.Literal(1L));

    var exception = Assert.Throws<RuntimeException>(() => MakeEvaluator(context).Eval(assignment, new ScriptEnvironment()));

    Assert.Contains("Undefined name 'missing'", exception.Message);
  }

  [Theory]
  [InlineData(ALKScriptTokenType.AmpAmp, "&&", false, false, false)]
  [InlineData(ALKScriptTokenType.AmpAmp, "&&", true, true, true)]
  [InlineData(ALKScriptTokenType.PipePipe, "||", true, false, true)]
  [InlineData(ALKScriptTokenType.PipePipe, "||", false, true, true)]
  public void Eval_LogicalOperators_ShortCircuitWithoutEvaluatingTheRightOperand(
    ALKScriptTokenType operatorType, string lexeme, bool leftValue, bool expectRightEvaluated, bool expectedResult)
  {
    // "&&"/"||" evaluate their operands by recursing into Eval() directly
    // (not through the context), so observe whether the right operand ran via
    // a real side effect — assigning to a variable predefined in the environment.
    var context = new FakeEvaluationContext();
    var environment = new ScriptEnvironment();
    environment.Define("evaluated", BoolValue.Of(false));
    var left = new LiteralExpr(Nodes.Token(ALKScriptTokenType.True, leftValue.ToString()), leftValue);
    var right = new AssignmentExpr(Nodes.Ident("evaluated"), new LiteralExpr(Nodes.Token(ALKScriptTokenType.True, "true"), true));
    var binary = new BinaryExpr(left, Nodes.Operator(operatorType, lexeme), right);

    var result = MakeEvaluator(context).Eval(binary, environment);

    Assert.True(environment.TryGet("evaluated", out var evaluatedFlag));
    Assert.Equal(expectRightEvaluated, Assert.IsType<BoolValue>(evaluatedFlag).Value);
    Assert.Equal(expectedResult, Assert.IsType<BoolValue>(result).Value);
  }

  [Theory]
  [InlineData(ALKScriptTokenType.Plus, "+", 3L)]
  [InlineData(ALKScriptTokenType.Minus, "-", -1L)]
  [InlineData(ALKScriptTokenType.Star, "*", 2L)]
  public void Eval_ArithmeticBinary_DelegatesToOperators(ALKScriptTokenType operatorType, string lexeme, long expected)
  {
    var context = new FakeEvaluationContext();
    var binary = new BinaryExpr(Nodes.Literal(1L), Nodes.Operator(operatorType, lexeme), Nodes.Literal(2L));

    var value = MakeEvaluator(context).Eval(binary, new ScriptEnvironment());

    Assert.Equal(expected, Assert.IsType<IntValue>(value).Value);
  }

  [Fact]
  public void Eval_UnaryBang_NegatesTruthiness()
  {
    var context = new FakeEvaluationContext();
    var unary = new UnaryExpr(Nodes.Operator(ALKScriptTokenType.Bang, "!"), Nodes.Literal(true));

    var value = MakeEvaluator(context).Eval(unary, new ScriptEnvironment());

    Assert.False(Assert.IsType<BoolValue>(value).Value);
  }

  [Fact]
  public void Eval_UnaryMinus_NegatesNumericOperand()
  {
    var context = new FakeEvaluationContext();
    var unary = new UnaryExpr(Nodes.Operator(ALKScriptTokenType.Minus, "-"), Nodes.Literal(5L));

    var value = MakeEvaluator(context).Eval(unary, new ScriptEnvironment());

    Assert.Equal(-5L, Assert.IsType<IntValue>(value).Value);
  }

  [Fact]
  public void Eval_UnaryMinus_OnNonNumericOperand_ThrowsRuntimeException()
  {
    var context = new FakeEvaluationContext();
    var unary = new UnaryExpr(Nodes.Operator(ALKScriptTokenType.Minus, "-"), Nodes.Literal("text"));

    Assert.Throws<RuntimeException>(() => MakeEvaluator(context).Eval(unary, new ScriptEnvironment()));
  }

  [Fact]
  public void Eval_Call_EvaluatesCalleeAndArgumentsThenDelegatesToTheContext()
  {
    var context = new FakeEvaluationContext();
    ALKScriptValue? capturedCallee = null;
    IReadOnlyList<ALKScriptValue>? capturedArguments = null;
    context.CallImpl = (callee, arguments, _) =>
    {
      capturedCallee = callee;
      capturedArguments = arguments;
      return new IntValue(99);
    };

    var call = new CallExpr(Nodes.Ident("f"), Nodes.Token(ALKScriptTokenType.RightParen, ")"), new[] { Nodes.Literal(1L), Nodes.Literal(2L) });
    var environment = new ScriptEnvironment();
    environment.Define("f", new IntValue(0)); // any value — Call delegation is what's under test

    var value = MakeEvaluator(context).Eval(call, environment);

    Assert.Equal(99L, Assert.IsType<IntValue>(value).Value);
    Assert.Equal(0L, Assert.IsType<IntValue>(capturedCallee).Value);
    Assert.Equal(new long[] { 1, 2 }, capturedArguments!.Select(a => ((IntValue)a).Value));
  }

  [Fact]
  public void Eval_New_EvaluatesArgumentsThenDelegatesConstructionToTheContext()
  {
    var classDecl = new ClassDecl(false, Nodes.Identifier("Foo"), System.Array.Empty<string>(), null, System.Array.Empty<TypeNode>(), System.Array.Empty<MemberDecl>());
    var classValue = new ClassValue(classDecl, null);
    var environment = new ScriptEnvironment();
    environment.Define("Foo", classValue);

    var context = new FakeEvaluationContext();
    ClassValue? capturedClass = null;
    context.ConstructImpl = (cls, arguments, _) => { capturedClass = cls; return new InstanceValue(cls); };

    var newExpr = new NewExpr(Nodes.Token(ALKScriptTokenType.New, "new"), Nodes.Identifier("Foo"), System.Array.Empty<TypeNode>(), new[] { Nodes.Literal(1L) });

    var value = MakeEvaluator(context).Eval(newExpr, environment);

    Assert.IsType<InstanceValue>(value);
    Assert.Same(classValue, capturedClass);
  }

  [Fact]
  public void Eval_NewWithNonClassTypeName_ThrowsRuntimeException()
  {
    var context = new FakeEvaluationContext();
    var environment = new ScriptEnvironment();
    environment.Define("NotAClass", new IntValue(1));
    var newExpr = new NewExpr(Nodes.Token(ALKScriptTokenType.New, "new"), Nodes.Identifier("NotAClass"), System.Array.Empty<TypeNode>(), System.Array.Empty<Expr>());

    var exception = Assert.Throws<RuntimeException>(() => MakeEvaluator(context).Eval(newExpr, environment));

    Assert.Contains("'NotAClass' is not a class", exception.Message);
  }

  [Fact]
  public void Eval_GetOnInstance_ReturnsFieldValue()
  {
    var classDecl = new ClassDecl(false, Nodes.Identifier("Foo"), System.Array.Empty<string>(), null, System.Array.Empty<TypeNode>(), System.Array.Empty<MemberDecl>());
    var instance = new InstanceValue(new ClassValue(classDecl, null));
    instance.Fields["name"] = new StringValue("Ada");
    var environment = new ScriptEnvironment();
    environment.Define("instance", instance);
    var get = new GetExpr(Nodes.Ident("instance"), Nodes.Identifier("name"));

    var context = new FakeEvaluationContext();
    var value = MakeEvaluator(context).Eval(get, environment);

    Assert.Equal("Ada", Assert.IsType<StringValue>(value).Value);
  }

  [Fact]
  public void Eval_GetOfUndefinedProperty_ThrowsRuntimeException()
  {
    var classDecl = new ClassDecl(false, Nodes.Identifier("Foo"), System.Array.Empty<string>(), null, System.Array.Empty<TypeNode>(), System.Array.Empty<MemberDecl>());
    var instance = new InstanceValue(new ClassValue(classDecl, null));
    var environment = new ScriptEnvironment();
    environment.Define("instance", instance);
    var get = new GetExpr(Nodes.Ident("instance"), Nodes.Identifier("missing"));

    var context = new FakeEvaluationContext();
    var exception = Assert.Throws<RuntimeException>(() => MakeEvaluator(context).Eval(get, environment));

    Assert.Contains("Undefined property 'missing'", exception.Message);
  }

  [Fact]
  public void Eval_Index_ReturnsTheElementAtThePosition()
  {
    var environment = new ScriptEnvironment();
    environment.Define("items", new ArrayValue(new List<ALKScriptValue> { new IntValue(10), new IntValue(20) }));
    var index = new IndexExpr(Nodes.Ident("items"), Nodes.Literal(1L), Nodes.Token(ALKScriptTokenType.RightBracket, "]"));

    var context = new FakeEvaluationContext();
    var value = MakeEvaluator(context).Eval(index, environment);

    Assert.Equal(20L, Assert.IsType<IntValue>(value).Value);
  }

  [Fact]
  public void Eval_IndexOutOfBounds_ThrowsRuntimeException()
  {
    var environment = new ScriptEnvironment();
    environment.Define("items", new ArrayValue(new List<ALKScriptValue> { new IntValue(10) }));
    var index = new IndexExpr(Nodes.Ident("items"), Nodes.Literal(5L), Nodes.Token(ALKScriptTokenType.RightBracket, "]"));

    var context = new FakeEvaluationContext();
    var exception = Assert.Throws<RuntimeException>(() => MakeEvaluator(context).Eval(index, environment));

    Assert.Contains("out of bounds", exception.Message);
  }

  [Fact]
  public void Eval_AlreadyPendingSignal_ShortCircuitsToNull()
  {
    var context = new FakeEvaluationContext { Signal = Signal.Return(NullValue.Instance) };

    var value = MakeEvaluator(context).Eval(Nodes.Literal(1L), new ScriptEnvironment());

    Assert.Same(NullValue.Instance, value);
  }
}
