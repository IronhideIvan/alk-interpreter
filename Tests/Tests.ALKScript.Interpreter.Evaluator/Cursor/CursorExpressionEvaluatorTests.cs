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
}
