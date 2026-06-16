using System.Collections.Generic;
using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Common.Evaluation;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Common.Token;
using ALKScript.Interpreter.Evaluator;
using ALKScript.Interpreter.Evaluator.Cursor;
using Tests.ALKScript.Interpreter.Evaluator.Unit;

namespace Tests.ALKScript.Interpreter.Evaluator.Cursor;

/// <summary>
/// Step 1 coverage for the cursor-rewrite plan (docs:
/// validated-nibbling-narwhal): literals, identifiers, this/base, grouping,
/// array literals, binary/unary/ternary operators, and member
/// access/indexing — all evaluated synchronously via
/// <see cref="EvaluationCursor.Eval"/> with no suspension possible, so every
/// <see cref="StepResult"/> here is <see cref="StepResult.Completed"/>.
/// </summary>
public class CursorExpressionEvaluatorTests
{
  private static EvaluationCursor MakeCursor() => new EvaluationCursor(new FunctionValueFactory());

  private static ALKScriptValue EvalCompleted(EvaluationCursor cursor, Expr expression, ScriptEnvironment environment)
  {
    var step = cursor.Eval(expression, environment);
    Assert.False(step.IsAwaiting);
    return step.Value!;
  }

  [Theory]
  [InlineData(null)]
  [InlineData(true)]
  [InlineData(1L)]
  [InlineData(1.5)]
  [InlineData("text")]
  public void Eval_Literal_ProducesTheMatchingValueType(object? literalValue)
  {
    var cursor = MakeCursor();
    var value = EvalCompleted(cursor, Nodes.Literal(literalValue), new ScriptEnvironment());

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
    var cursor = MakeCursor();
    var environment = new ScriptEnvironment();
    environment.Define("x", new IntValue(9));

    var value = EvalCompleted(cursor, Nodes.Ident("x"), environment);

    Assert.Equal(9L, Assert.IsType<IntValue>(value).Value);
  }

  [Fact]
  public void Eval_UndefinedIdentifier_ThrowsRuntimeException()
  {
    var cursor = MakeCursor();

    var exception = Assert.Throws<RuntimeException>(() => cursor.Eval(Nodes.Ident("missing"), new ScriptEnvironment()));

    Assert.Contains("Undefined name 'missing'", exception.Message);
  }

  [Fact]
  public void Eval_Grouping_DelegatesToTheInnerExpression()
  {
    var cursor = MakeCursor();

    var value = EvalCompleted(cursor, new GroupingExpr(Nodes.Literal(3L)), new ScriptEnvironment());

    Assert.Equal(3L, Assert.IsType<IntValue>(value).Value);
  }

  [Fact]
  public void Eval_ArrayLiteral_EvaluatesEachElementInOrder()
  {
    var cursor = MakeCursor();
    var elements = new List<Expr> { Nodes.Literal(1L), Nodes.Literal(2L), Nodes.Literal(3L) };

    var value = Assert.IsType<ArrayValue>(EvalCompleted(cursor, new ArrayLiteralExpr(elements), new ScriptEnvironment()));

    Assert.Equal(new long[] { 1, 2, 3 }, value.Items.ConvertAll(item => ((IntValue)item).Value));
  }

  [Fact]
  public void Eval_BinaryAdd_AddsTwoInts()
  {
    var cursor = MakeCursor();
    var expression = new BinaryExpr(Nodes.Literal(1L), Nodes.Operator(ALKScriptTokenType.Plus, "+"), Nodes.Literal(2L));

    var value = EvalCompleted(cursor, expression, new ScriptEnvironment());

    Assert.Equal(3L, Assert.IsType<IntValue>(value).Value);
  }

  [Fact]
  public void Eval_LogicalAnd_ShortCircuitsOnFalseLeft()
  {
    var cursor = MakeCursor();
    var expression = new BinaryExpr(
      Nodes.Literal(false),
      Nodes.Operator(ALKScriptTokenType.AmpAmp, "&&"),
      Nodes.Ident("missing")); // would throw if evaluated

    var value = EvalCompleted(cursor, expression, new ScriptEnvironment());

    Assert.False(Assert.IsType<BoolValue>(value).Value);
  }

  [Fact]
  public void Eval_UnaryMinus_NegatesAnInt()
  {
    var cursor = MakeCursor();
    var expression = new UnaryExpr(Nodes.Operator(ALKScriptTokenType.Minus, "-"), Nodes.Literal(5L));

    var value = EvalCompleted(cursor, expression, new ScriptEnvironment());

    Assert.Equal(-5L, Assert.IsType<IntValue>(value).Value);
  }

  [Fact]
  public void Eval_Ternary_EvaluatesOnlyTheChosenBranch()
  {
    var cursor = MakeCursor();
    var expression = new TernaryExpr(
      Nodes.Literal(true),
      Nodes.Operator(ALKScriptTokenType.Question, "?"),
      Nodes.Literal(1L),
      Nodes.Ident("missing")); // would throw if evaluated

    var value = EvalCompleted(cursor, expression, new ScriptEnvironment());

    Assert.Equal(1L, Assert.IsType<IntValue>(value).Value);
  }

  [Fact]
  public void Eval_Index_ReturnsTheArrayElement()
  {
    var cursor = MakeCursor();
    var environment = new ScriptEnvironment();
    environment.Define("arr", new ArrayValue(new List<ALKScriptValue> { new IntValue(10), new IntValue(20) }));

    var expression = new IndexExpr(Nodes.Ident("arr"), Nodes.Literal(1L), Nodes.Token(ALKScriptTokenType.RightBracket, "]"));

    var value = EvalCompleted(cursor, expression, environment);

    Assert.Equal(20L, Assert.IsType<IntValue>(value).Value);
  }

  [Fact]
  public void Eval_GetArrayLength_ReturnsItemCount()
  {
    var cursor = MakeCursor();
    var environment = new ScriptEnvironment();
    environment.Define("arr", new ArrayValue(new List<ALKScriptValue> { new IntValue(10), new IntValue(20), new IntValue(30) }));

    var expression = new GetExpr(Nodes.Ident("arr"), Nodes.Identifier("length"));

    var value = EvalCompleted(cursor, expression, environment);

    Assert.Equal(3L, Assert.IsType<IntValue>(value).Value);
  }

  [Fact]
  public void Eval_AssignmentToIdentifier_UpdatesTheBindingAndReturnsTheValue()
  {
    var cursor = MakeCursor();
    var environment = new ScriptEnvironment();
    environment.Define("x", new IntValue(1));

    var assignment = new AssignmentExpr(Nodes.Ident("x"), Nodes.Literal(2L));

    var value = EvalCompleted(cursor, assignment, environment);

    Assert.Equal(2L, Assert.IsType<IntValue>(value).Value);
    Assert.True(environment.TryGet("x", out var stored));
    Assert.Equal(2L, Assert.IsType<IntValue>(stored).Value);
  }

  [Fact]
  public void Eval_AssignmentToUndefinedIdentifier_ThrowsRuntimeException()
  {
    var cursor = MakeCursor();
    var assignment = new AssignmentExpr(Nodes.Ident("missing"), Nodes.Literal(1L));

    var exception = Assert.Throws<RuntimeException>(() => cursor.Eval(assignment, new ScriptEnvironment()));

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
    var cursor = MakeCursor();
    var environment = new ScriptEnvironment();
    environment.Define("evaluated", BoolValue.Of(false));
    var left = new LiteralExpr(Nodes.Token(ALKScriptTokenType.True, leftValue.ToString()), leftValue);
    var right = new AssignmentExpr(Nodes.Ident("evaluated"), new LiteralExpr(Nodes.Token(ALKScriptTokenType.True, "true"), true));
    var binary = new BinaryExpr(left, Nodes.Operator(operatorType, lexeme), right);

    var result = EvalCompleted(cursor, binary, environment);

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
    var cursor = MakeCursor();
    var binary = new BinaryExpr(Nodes.Literal(1L), Nodes.Operator(operatorType, lexeme), Nodes.Literal(2L));

    var value = EvalCompleted(cursor, binary, new ScriptEnvironment());

    Assert.Equal(expected, Assert.IsType<IntValue>(value).Value);
  }

  [Fact]
  public void Eval_UnaryBang_NegatesTruthiness()
  {
    var cursor = MakeCursor();
    var unary = new UnaryExpr(Nodes.Operator(ALKScriptTokenType.Bang, "!"), Nodes.Literal(true));

    var value = EvalCompleted(cursor, unary, new ScriptEnvironment());

    Assert.False(Assert.IsType<BoolValue>(value).Value);
  }

  [Fact]
  public void Eval_UnaryMinus_OnNonNumericOperand_ThrowsRuntimeException()
  {
    var cursor = MakeCursor();
    var unary = new UnaryExpr(Nodes.Operator(ALKScriptTokenType.Minus, "-"), Nodes.Literal("text"));

    Assert.Throws<RuntimeException>(() => cursor.Eval(unary, new ScriptEnvironment()));
  }

  [Fact]
  public void Eval_NewWithNonClassTypeName_ThrowsRuntimeException()
  {
    var cursor = MakeCursor();
    var environment = new ScriptEnvironment();
    environment.Define("NotAClass", new IntValue(1));
    var newExpr = new NewExpr(Nodes.Token(ALKScriptTokenType.New, "new"), Nodes.Identifier("NotAClass"), System.Array.Empty<TypeNode>(), System.Array.Empty<Expr>());

    var exception = Assert.Throws<RuntimeException>(() => cursor.Eval(newExpr, environment));

    Assert.Contains("'NotAClass' is not a class", exception.Message);
  }

  [Fact]
  public void Eval_GetOnInstance_ReturnsFieldValue()
  {
    var cursor = MakeCursor();
    var classDecl = new ClassDecl(false, Nodes.Identifier("Foo"), System.Array.Empty<string>(), null, System.Array.Empty<TypeNode>(), System.Array.Empty<MemberDecl>());
    var instance = new InstanceValue(new ClassValue(classDecl, null, new ScriptEnvironment()));
    instance.Fields["name"] = new StringValue("Ada");
    var environment = new ScriptEnvironment();
    environment.Define("instance", instance);
    var get = new GetExpr(Nodes.Ident("instance"), Nodes.Identifier("name"));

    var value = EvalCompleted(cursor, get, environment);

    Assert.Equal("Ada", Assert.IsType<StringValue>(value).Value);
  }

  [Fact]
  public void Eval_GetOfUndefinedProperty_ThrowsRuntimeException()
  {
    var cursor = MakeCursor();
    var classDecl = new ClassDecl(false, Nodes.Identifier("Foo"), System.Array.Empty<string>(), null, System.Array.Empty<TypeNode>(), System.Array.Empty<MemberDecl>());
    var instance = new InstanceValue(new ClassValue(classDecl, null, new ScriptEnvironment()));
    var environment = new ScriptEnvironment();
    environment.Define("instance", instance);
    var get = new GetExpr(Nodes.Ident("instance"), Nodes.Identifier("missing"));

    var exception = Assert.Throws<RuntimeException>(() => cursor.Eval(get, environment));

    Assert.Contains("Undefined property 'missing'", exception.Message);
  }

  [Fact]
  public void Eval_IndexOutOfBounds_ThrowsRuntimeException()
  {
    var cursor = MakeCursor();
    var environment = new ScriptEnvironment();
    environment.Define("items", new ArrayValue(new List<ALKScriptValue> { new IntValue(10) }));
    var index = new IndexExpr(Nodes.Ident("items"), Nodes.Literal(5L), Nodes.Token(ALKScriptTokenType.RightBracket, "]"));

    var exception = Assert.Throws<RuntimeException>(() => cursor.Eval(index, environment));

    Assert.Contains("out of bounds", exception.Message);
  }

  [Fact]
  public void Eval_TypeTest_ArrayOfInt_TrueWhenAllElementsAreInts()
  {
    var cursor = MakeCursor();
    var environment = new ScriptEnvironment();
    environment.Define("items", new ArrayValue(new List<ALKScriptValue> { new IntValue(1), new IntValue(2) }));

    var intArrayType = new TypeNode("int", System.Array.Empty<TypeNode>(), arrayRank: 1, isNullable: false);
    var typeTest = new TypeTestExpr(Nodes.Ident("items"), Nodes.Token(ALKScriptTokenType.Is, "is"), intArrayType);

    var result = EvalCompleted(cursor, typeTest, environment);

    Assert.True(Assert.IsType<BoolValue>(result).Value);
  }

  [Fact]
  public void Eval_TypeTest_ArrayOfInt_FalseWhenElementTypeDoesNotMatch()
  {
    var cursor = MakeCursor();
    var environment = new ScriptEnvironment();
    environment.Define("items", new ArrayValue(new List<ALKScriptValue> { new StringValue("a"), new StringValue("b") }));

    var intArrayType = new TypeNode("int", System.Array.Empty<TypeNode>(), arrayRank: 1, isNullable: false);
    var typeTest = new TypeTestExpr(Nodes.Ident("items"), Nodes.Token(ALKScriptTokenType.Is, "is"), intArrayType);

    var result = EvalCompleted(cursor, typeTest, environment);

    Assert.False(Assert.IsType<BoolValue>(result).Value);
  }

  [Theory]
  [InlineData(42L, "int")]
  [InlineData("hello", "string")]
  [InlineData(null, "null")]
  [InlineData(true, "bool")]
  [InlineData(1.5, "float")]
  public void Eval_Typeof_PrimitiveOperand_ReturnsTypeName(object? operand, string expectedTypeName)
  {
    var cursor = MakeCursor();
    var keyword = Nodes.Token(ALKScriptTokenType.Typeof, "typeof");
    var expr = new TypeofExpr(keyword, Nodes.Literal(operand));

    var result = EvalCompleted(cursor, expr, new ScriptEnvironment());

    Assert.Equal(expectedTypeName, Assert.IsType<StringValue>(result).Value);
  }

  [Fact]
  public void Eval_Typeof_ArrayOperand_ReturnsArray()
  {
    var cursor = MakeCursor();
    var keyword = Nodes.Token(ALKScriptTokenType.Typeof, "typeof");
    var expr = new TypeofExpr(keyword, new ArrayLiteralExpr(new List<Expr> { Nodes.Literal(1L) }));

    var result = EvalCompleted(cursor, expr, new ScriptEnvironment());

    Assert.Equal("array", Assert.IsType<StringValue>(result).Value);
  }

  [Fact]
  public void Eval_Typeof_InstanceOperand_ReturnsClassName()
  {
    var cursor = MakeCursor();
    var classDecl = new ClassDecl(false, Nodes.Identifier("Point"), System.Array.Empty<string>(), null, System.Array.Empty<TypeNode>(), System.Array.Empty<MemberDecl>());
    var instance = new InstanceValue(new ClassValue(classDecl, null, new ScriptEnvironment()));
    var environment = new ScriptEnvironment();
    environment.Define("p", instance);
    var keyword = Nodes.Token(ALKScriptTokenType.Typeof, "typeof");
    var expr = new TypeofExpr(keyword, Nodes.Ident("p"));

    var result = EvalCompleted(cursor, expr, environment);

    Assert.Equal("Point", Assert.IsType<StringValue>(result).Value);
  }

  [Fact]
  public void Eval_AlreadyPendingSignal_ShortCircuitsToNull()
  {
    var cursor = MakeCursor();
    cursor.Signal = Signal.Return(NullValue.Instance);

    var value = EvalCompleted(cursor, Nodes.Literal(1L), new ScriptEnvironment());

    Assert.Same(NullValue.Instance, value);
  }
}
