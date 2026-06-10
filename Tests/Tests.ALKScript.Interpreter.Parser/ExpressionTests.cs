using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Common.Token;
using ALKScript.Interpreter.Parser;

namespace Tests.ALKScript.Interpreter.Parser;

public class ExpressionTests : ParserTestBase
{
  private static Expr SingleExpressionStatement(ProgramNode program)
  {
    var stmtDecl = Assert.IsType<StatementDecl>(Assert.Single(program.Declarations));
    var exprStmt = Assert.IsType<ExpressionStmt>(stmtDecl.Statement);
    return exprStmt.Expression;
  }

  [Fact]
  public void Parse_ArithmeticExpression_RespectsOperatorPrecedence()
  {
    var program = Parse("1 + 2 * 3;");

    var binary = Assert.IsType<BinaryExpr>(SingleExpressionStatement(program));
    Assert.Equal(ALKScriptTokenType.Plus, binary.Operator.Type);

    var left = Assert.IsType<LiteralExpr>(binary.Left);
    Assert.Equal(1, left.Value);

    var right = Assert.IsType<BinaryExpr>(binary.Right);
    Assert.Equal(ALKScriptTokenType.Star, right.Operator.Type);
  }

  [Fact]
  public void Parse_ComparisonAndLogicalExpression_BuildsNestedBinaryExpressions()
  {
    var program = Parse("a < b && b < c;");

    var logical = Assert.IsType<BinaryExpr>(SingleExpressionStatement(program));
    Assert.Equal(ALKScriptTokenType.AmpAmp, logical.Operator.Type);
    Assert.IsType<BinaryExpr>(logical.Left);
    Assert.IsType<BinaryExpr>(logical.Right);
  }

  [Fact]
  public void Parse_AssignmentExpression_IsRightAssociative()
  {
    var program = Parse("a = b = 1;");

    var outer = Assert.IsType<AssignmentExpr>(SingleExpressionStatement(program));
    Assert.IsType<IdentifierExpr>(outer.Target);

    var inner = Assert.IsType<AssignmentExpr>(outer.Value);
    Assert.IsType<IdentifierExpr>(inner.Target);
    Assert.IsType<LiteralExpr>(inner.Value);
  }

  [Fact]
  public void Parse_UnaryExpression_ProducesUnaryExpr()
  {
    var program = Parse("!ready;");

    var unary = Assert.IsType<UnaryExpr>(SingleExpressionStatement(program));
    Assert.Equal(ALKScriptTokenType.Bang, unary.Operator.Type);
    Assert.IsType<IdentifierExpr>(unary.Operand);
  }

  [Fact]
  public void Parse_AwaitExpression_ProducesAwaitExpr()
  {
    var program = Parse("await fetchValue();");

    var awaitExpr = Assert.IsType<AwaitExpr>(SingleExpressionStatement(program));
    Assert.IsType<CallExpr>(awaitExpr.Operand);
  }

  [Fact]
  public void Parse_AwaitExpressionAtTopLevel_IsAllowed()
  {
    // The entry module's top level runs as part of the (Task-returning)
    // overall program evaluation and can itself genuinely suspend on an
    // "await" — so it counts as an async context (see ASYNC_AWAIT_DESIGN.md
    // decisions #4/#9) even though it isn't textually inside an "async"
    // function/method body.
    var program = Parse("await fetchValue();");

    Assert.IsType<AwaitExpr>(SingleExpressionStatement(program));
  }

  [Fact]
  public void Parse_AwaitExpressionInsideAsyncFunctionBody_IsAllowed()
  {
    var program = Parse("async function void main() {\n  await fetchValue();\n}");

    var function = Assert.IsType<FunctionDecl>(Assert.Single(program.Declarations));
    var stmtDecl = Assert.IsType<StatementDecl>(Assert.Single(function.Body!.Statements));
    var exprStmt = Assert.IsType<ExpressionStmt>(stmtDecl.Statement);
    Assert.IsType<AwaitExpr>(exprStmt.Expression);
  }

  [Fact]
  public void Parse_AwaitExpressionInsideNonAsyncFunctionBody_ThrowsParseException()
  {
    var exception = Assert.Throws<ParseException>(() => Parse("function void main() {\n  await fetchValue();\n}"));

    Assert.Contains("'await' is only valid inside an 'async' function or method.", exception.Message);
  }

  [Fact]
  public void Parse_AwaitExpressionInsideNonAsyncMethodNestedInAsyncOne_ThrowsParseException()
  {
    // Saving/restoring (rather than just setting a flag) on entering each
    // body is what makes a non-"async" method nested inside an "async" one
    // correctly rejected — its own context governs, not its enclosing one's.
    // 'load' is async with an 'await' inside it (satisfying the "async
    // requires await" rule); the ParseException comes from 'helper', which is
    // non-async yet contains an 'await' expression.
    Assert.Throws<ParseException>(() => Parse(
      "class Loader {\n" +
      "  async function void load() { await fetchValue(); }\n" +
      "  function void helper() {\n" +
      "    await fetchValue();\n" +
      "  }\n" +
      "}"));
  }

  [Fact]
  public void Parse_AwaitExpressionInsideConstructorBody_ThrowsParseException()
  {
    Assert.Throws<ParseException>(() => Parse(
      "class Loader {\n" +
      "  new() {\n" +
      "    await fetchValue();\n" +
      "  }\n" +
      "}"));
  }

  [Fact]
  public void Parse_CallChainedWithMemberAndIndexAccess_BuildsNestedCallExpressions()
  {
    var program = Parse("names.push(values[0].length);");

    var call = Assert.IsType<CallExpr>(SingleExpressionStatement(program));
    var callee = Assert.IsType<GetExpr>(call.Callee);
    Assert.Equal("push", callee.Name.Lexeme);

    var argument = Assert.Single(call.Arguments);
    var argumentGet = Assert.IsType<GetExpr>(argument);
    Assert.Equal("length", argumentGet.Name.Lexeme);

    var index = Assert.IsType<IndexExpr>(argumentGet.Target);
    var indexLiteral = Assert.IsType<LiteralExpr>(index.Index);
    Assert.Equal(0, indexLiteral.Value);
  }

  // ── Array API member/method expressions ────────────────────────────────────
  //
  // The array API (length, push, pop, remove, join, slice) introduces no new
  // grammar — each call parses as an ordinary CallExpr whose callee is a
  // GetExpr on the array expression. These tests pin down that shape for
  // every member of the API so a future grammar change can't silently alter
  // how they parse.

  [Theory]
  [InlineData("items.push(4);", "push", 1)]
  [InlineData("items.pop();", "pop", 0)]
  [InlineData("items.remove(0);", "remove", 1)]
  [InlineData("items.join(other);", "join", 1)]
  [InlineData("items.slice(1, 2);", "slice", 2)]
  public void Parse_ArrayApiMethodCall_ProducesCallExprWithGetExprCallee(
    string source, string methodName, int argumentCount)
  {
    var program = Parse(source);

    var call = Assert.IsType<CallExpr>(SingleExpressionStatement(program));
    var callee = Assert.IsType<GetExpr>(call.Callee);

    Assert.Equal(methodName, callee.Name.Lexeme);
    Assert.IsType<IdentifierExpr>(callee.Target);
    Assert.Equal(argumentCount, call.Arguments.Count);
  }

  [Fact]
  public void Parse_ArrayLength_ProducesGetExprWithoutCall()
  {
    // 'length' is a property, not a method — it must NOT be parsed as a call.
    var program = Parse("items.length;");

    var get = Assert.IsType<GetExpr>(SingleExpressionStatement(program));
    Assert.Equal("length", get.Name.Lexeme);
    Assert.IsType<IdentifierExpr>(get.Target);
  }

  [Fact]
  public void Parse_ChainedArrayApiCalls_NestCalleeTargets()
  {
    // "items.slice(1, 2).join(more)" — the outer call's callee targets the
    // inner call, mirroring any other method-chaining expression.
    var program = Parse("items.slice(1, 2).join(more);");

    var outerCall = Assert.IsType<CallExpr>(SingleExpressionStatement(program));
    var outerCallee = Assert.IsType<GetExpr>(outerCall.Callee);
    Assert.Equal("join", outerCallee.Name.Lexeme);

    var innerCall = Assert.IsType<CallExpr>(outerCallee.Target);
    var innerCallee = Assert.IsType<GetExpr>(innerCall.Callee);
    Assert.Equal("slice", innerCallee.Name.Lexeme);
    Assert.Equal(2, innerCall.Arguments.Count);
  }

  [Fact]
  public void Parse_ArrayPushReturnValueLengthAccess_ChainsGetExprOverCallExpr()
  {
    // "items.push(4).length" — accessing a property on the result of a call.
    var program = Parse("items.push(4).length;");

    var get = Assert.IsType<GetExpr>(SingleExpressionStatement(program));
    Assert.Equal("length", get.Name.Lexeme);

    var call = Assert.IsType<CallExpr>(get.Target);
    var callee = Assert.IsType<GetExpr>(call.Callee);
    Assert.Equal("push", callee.Name.Lexeme);
    Assert.Single(call.Arguments);
  }

  // ── Bitwise and shift operators ─────────────────────────────────────────────

  [Fact]
  public void Parse_BitwiseOperators_RespectPrecedenceRelativeToEquality()
  {
    // "a == b | c ^ d & e" parses as "(a == b) | (c ^ (d & e))" — '&' binds
    // tightest, then '^', then '|', all looser than equality ('==').
    var program = Parse("a == b | c ^ d & e;");

    var or = Assert.IsType<BinaryExpr>(SingleExpressionStatement(program));
    Assert.Equal(ALKScriptTokenType.Pipe, or.Operator.Type);

    var equality = Assert.IsType<BinaryExpr>(or.Left);
    Assert.Equal(ALKScriptTokenType.EqualEqual, equality.Operator.Type);

    var xor = Assert.IsType<BinaryExpr>(or.Right);
    Assert.Equal(ALKScriptTokenType.Caret, xor.Operator.Type);

    var and = Assert.IsType<BinaryExpr>(xor.Right);
    Assert.Equal(ALKScriptTokenType.Amp, and.Operator.Type);
  }

  [Fact]
  public void Parse_BitwiseAndExpression_LooserThanLogicalAnd_LooserThanEquality()
  {
    // "a && b & c == d" parses as "a && (b & (c == d))".
    var program = Parse("a && b & c == d;");

    var logicalAnd = Assert.IsType<BinaryExpr>(SingleExpressionStatement(program));
    Assert.Equal(ALKScriptTokenType.AmpAmp, logicalAnd.Operator.Type);

    var bitwiseAnd = Assert.IsType<BinaryExpr>(logicalAnd.Right);
    Assert.Equal(ALKScriptTokenType.Amp, bitwiseAnd.Operator.Type);

    var equality = Assert.IsType<BinaryExpr>(bitwiseAnd.Right);
    Assert.Equal(ALKScriptTokenType.EqualEqual, equality.Operator.Type);
  }

  [Theory]
  [InlineData("a << b;", ALKScriptTokenType.LessLess)]
  [InlineData("a >> b;", ALKScriptTokenType.GreaterGreater)]
  public void Parse_ShiftExpression_ProducesBinaryExpr(string source, ALKScriptTokenType expectedOp)
  {
    var program = Parse(source);

    var binary = Assert.IsType<BinaryExpr>(SingleExpressionStatement(program));
    Assert.Equal(expectedOp, binary.Operator.Type);
    Assert.IsType<IdentifierExpr>(binary.Left);
    Assert.IsType<IdentifierExpr>(binary.Right);
  }

  [Fact]
  public void Parse_ShiftExpression_IsLooserThanAdditionAndTighterThanComparison()
  {
    // "a + b << c < d" parses as "((a + b) << c) < d".
    var program = Parse("a + b << c < d;");

    var comparison = Assert.IsType<BinaryExpr>(SingleExpressionStatement(program));
    Assert.Equal(ALKScriptTokenType.Less, comparison.Operator.Type);

    var shift = Assert.IsType<BinaryExpr>(comparison.Left);
    Assert.Equal(ALKScriptTokenType.LessLess, shift.Operator.Type);

    var addition = Assert.IsType<BinaryExpr>(shift.Left);
    Assert.Equal(ALKScriptTokenType.Plus, addition.Operator.Type);
  }

  [Fact]
  public void Parse_BitwiseNot_ProducesUnaryExpr()
  {
    var program = Parse("~flags;");

    var unary = Assert.IsType<UnaryExpr>(SingleExpressionStatement(program));
    Assert.Equal(ALKScriptTokenType.Tilde, unary.Operator.Type);
    Assert.IsType<IdentifierExpr>(unary.Operand);
  }

  [Theory]
  [InlineData("flags &= mask;", ALKScriptTokenType.AmpEqual)]
  [InlineData("flags |= mask;", ALKScriptTokenType.PipeEqual)]
  [InlineData("flags ^= mask;", ALKScriptTokenType.CaretEqual)]
  [InlineData("flags <<= 1;",   ALKScriptTokenType.LessLessEqual)]
  [InlineData("flags >>= 1;",   ALKScriptTokenType.GreaterGreaterEqual)]
  public void Parse_CompoundBitwiseAssignment_ProducesCompoundAssignmentExpr(string source, ALKScriptTokenType expectedOp)
  {
    var program = Parse(source);

    var compound = Assert.IsType<CompoundAssignmentExpr>(SingleExpressionStatement(program));
    Assert.Equal(expectedOp, compound.Operator.Type);
    Assert.IsType<IdentifierExpr>(compound.Target);
  }

  // ── String interpolation ────────────────────────────────────────────────────

  [Fact]
  public void Parse_TemplateStringWithoutInterpolation_ProducesInterpolatedStringExprWithSinglePart()
  {
    var program = Parse("`hello world`;");

    var interpolated = Assert.IsType<InterpolatedStringExpr>(SingleExpressionStatement(program));
    Assert.Equal(new[] { "hello world" }, interpolated.Parts);
    Assert.Empty(interpolated.Expressions);
  }

  [Fact]
  public void Parse_TemplateStringWithInterpolation_ProducesPartsAndExpressions()
  {
    var program = Parse("`Hello, ${name}!`;");

    var interpolated = Assert.IsType<InterpolatedStringExpr>(SingleExpressionStatement(program));
    Assert.Equal(new[] { "Hello, ", "!" }, interpolated.Parts);

    var identifier = Assert.IsType<IdentifierExpr>(Assert.Single(interpolated.Expressions));
    Assert.Equal("name", identifier.Name.Lexeme);
  }

  [Fact]
  public void Parse_TemplateStringWithMultipleInterpolations_ProducesAllPartsAndExpressions()
  {
    var program = Parse("`${a} + ${b} = ${a + b}`;");

    var interpolated = Assert.IsType<InterpolatedStringExpr>(SingleExpressionStatement(program));
    Assert.Equal(new[] { "", " + ", " = ", "" }, interpolated.Parts);
    Assert.Equal(3, interpolated.Expressions.Count);

    Assert.IsType<IdentifierExpr>(interpolated.Expressions[0]);
    Assert.IsType<IdentifierExpr>(interpolated.Expressions[1]);
    Assert.IsType<BinaryExpr>(interpolated.Expressions[2]);
  }

  [Fact]
  public void Parse_NewExpression_ProducesNewExpr()
  {
    var program = Parse("new Person(\"Ada\", 36);");

    var newExpr = Assert.IsType<NewExpr>(SingleExpressionStatement(program));
    Assert.Equal("Person", newExpr.TypeName.Lexeme);
    Assert.Equal(2, newExpr.Arguments.Count);
  }

  [Fact]
  public void Parse_GroupedExpression_ProducesGroupingExpr()
  {
    var program = Parse("(1 + 2) * 3;");

    var binary = Assert.IsType<BinaryExpr>(SingleExpressionStatement(program));
    Assert.Equal(ALKScriptTokenType.Star, binary.Operator.Type);
    Assert.IsType<GroupingExpr>(binary.Left);
  }

  [Fact]
  public void Parse_ThisAndBaseExpressions_ProduceExpectedNodes()
  {
    var thisProgram = Parse("this.name;");
    var thisGet = Assert.IsType<GetExpr>(SingleExpressionStatement(thisProgram));
    Assert.IsType<ThisExpr>(thisGet.Target);

    var baseProgram = Parse("base.greet();");
    var baseCall = Assert.IsType<CallExpr>(SingleExpressionStatement(baseProgram));
    var baseGet = Assert.IsType<GetExpr>(baseCall.Callee);
    Assert.IsType<BaseExpr>(baseGet.Target);
  }

  // ── Prefix / postfix update expressions ──────────────────────────────────

  [Theory]
  [InlineData("++x;",  ALKScriptTokenType.PlusPlus)]
  [InlineData("--x;",  ALKScriptTokenType.MinusMinus)]
  public void Parse_PrefixUpdate_ProducesPrefixUpdateExprWithCorrectOperator(string source, ALKScriptTokenType expectedOp)
  {
    var program = Parse(source);

    var stmtDecl = Assert.IsType<StatementDecl>(Assert.Single(program.Declarations));
    var exprStmt = Assert.IsType<ExpressionStmt>(stmtDecl.Statement);
    var prefix = Assert.IsType<PrefixUpdateExpr>(exprStmt.Expression);

    Assert.Equal(expectedOp, prefix.Operator.Type);
    Assert.IsType<IdentifierExpr>(prefix.Operand);
  }

  [Theory]
  [InlineData("x++;",  ALKScriptTokenType.PlusPlus)]
  [InlineData("x--;",  ALKScriptTokenType.MinusMinus)]
  public void Parse_PostfixUpdate_ProducesPostfixUpdateExprWithCorrectOperator(string source, ALKScriptTokenType expectedOp)
  {
    var program = Parse(source);

    var stmtDecl = Assert.IsType<StatementDecl>(Assert.Single(program.Declarations));
    var exprStmt = Assert.IsType<ExpressionStmt>(stmtDecl.Statement);
    var postfix = Assert.IsType<PostfixUpdateExpr>(exprStmt.Expression);

    Assert.Equal(expectedOp, postfix.Operator.Type);
    Assert.IsType<IdentifierExpr>(postfix.Operand);
  }

  [Fact]
  public void Parse_PostfixUpdate_OnMemberAccess_BindsToWholeChain()
  {
    // "obj.count++" should parse as (obj.count)++ — the postfix binds
    // to the fully resolved chain, not just "count".
    var program = Parse("obj.count++;");

    var stmtDecl = Assert.IsType<StatementDecl>(Assert.Single(program.Declarations));
    var exprStmt = Assert.IsType<ExpressionStmt>(stmtDecl.Statement);
    var postfix = Assert.IsType<PostfixUpdateExpr>(exprStmt.Expression);

    Assert.Equal(ALKScriptTokenType.PlusPlus, postfix.Operator.Type);
    var get = Assert.IsType<GetExpr>(postfix.Operand);
    Assert.Equal("count", get.Name.Lexeme);
  }

  [Fact]
  public void Parse_PrefixUpdate_OnIndexExpression_IsValid()
  {
    var program = Parse("++arr[0];");

    var stmtDecl = Assert.IsType<StatementDecl>(Assert.Single(program.Declarations));
    var exprStmt = Assert.IsType<ExpressionStmt>(stmtDecl.Statement);
    var prefix = Assert.IsType<PrefixUpdateExpr>(exprStmt.Expression);

    Assert.IsType<IndexExpr>(prefix.Operand);
  }

  [Theory]
  [InlineData("1++;",    "++")]
  [InlineData("(a+b)++;","++")]
  [InlineData("--1;",    "--")]
  public void Parse_UpdateOnNonAssignableTarget_ThrowsParseException(string source, string op)
  {
    var ex = Assert.Throws<ParseException>(() => Parse(source));
    Assert.Contains(op, ex.Message);
    Assert.Contains("assignable target", ex.Message);
  }

  // ── Compound assignment ───────────────────────────────────────────────────

  [Theory]
  [InlineData("x += 1;",  ALKScriptTokenType.PlusEqual)]
  [InlineData("x -= 1;",  ALKScriptTokenType.MinusEqual)]
  [InlineData("x *= 2;",  ALKScriptTokenType.StarEqual)]
  [InlineData("x /= 2;",  ALKScriptTokenType.SlashEqual)]
  [InlineData("x %= 3;",  ALKScriptTokenType.PercentEqual)]
  public void Parse_CompoundAssignment_ProducesCompoundAssignmentExprWithCorrectOperator(
    string source, ALKScriptTokenType expectedOp)
  {
    var program = Parse(source);

    var stmtDecl = Assert.IsType<StatementDecl>(Assert.Single(program.Declarations));
    var exprStmt = Assert.IsType<ExpressionStmt>(stmtDecl.Statement);
    var compound = Assert.IsType<CompoundAssignmentExpr>(exprStmt.Expression);

    Assert.Equal(expectedOp, compound.Operator.Type);
    Assert.IsType<IdentifierExpr>(compound.Target);
    Assert.IsType<LiteralExpr>(compound.Value);
  }

  // ── Ternary operator ─────────────────────────────────────────────────────

  [Fact]
  public void Parse_TernaryExpression_ProducesTernaryExprWithCorrectBranches()
  {
    var program = Parse("a ? b : c;");

    var stmtDecl = Assert.IsType<StatementDecl>(Assert.Single(program.Declarations));
    var exprStmt = Assert.IsType<ExpressionStmt>(stmtDecl.Statement);
    var ternary = Assert.IsType<TernaryExpr>(exprStmt.Expression);

    Assert.IsType<IdentifierExpr>(ternary.Condition);
    Assert.IsType<IdentifierExpr>(ternary.ThenExpr);
    Assert.IsType<IdentifierExpr>(ternary.ElseExpr);
  }

  [Fact]
  public void Parse_TernaryExpression_IsRightAssociativeInBranches()
  {
    // "a ? b : c ? d : e" should parse as "a ? b : (c ? d : e)"
    var program = Parse("a ? b : c ? d : e;");

    var stmtDecl = Assert.IsType<StatementDecl>(Assert.Single(program.Declarations));
    var exprStmt = Assert.IsType<ExpressionStmt>(stmtDecl.Statement);
    var outer = Assert.IsType<TernaryExpr>(exprStmt.Expression);

    Assert.IsType<IdentifierExpr>(outer.Condition);  // a
    Assert.IsType<IdentifierExpr>(outer.ThenExpr);   // b
    Assert.IsType<TernaryExpr>(outer.ElseExpr);      // c ? d : e
  }

  // ── Null coalescing (??) ─────────────────────────────────────────────────

  [Fact]
  public void Parse_NullCoalescing_ProducesBinaryExprWithQuestionQuestionOperator()
  {
    var program = Parse("x ?? y;");

    var stmtDecl = Assert.IsType<StatementDecl>(Assert.Single(program.Declarations));
    var exprStmt = Assert.IsType<ExpressionStmt>(stmtDecl.Statement);
    var binary = Assert.IsType<BinaryExpr>(exprStmt.Expression);

    Assert.Equal(ALKScriptTokenType.QuestionQuestion, binary.Operator.Type);
    Assert.IsType<IdentifierExpr>(binary.Left);
    Assert.IsType<IdentifierExpr>(binary.Right);
  }

  // ── Null-conditional (?.) ────────────────────────────────────────────────

  [Fact]
  public void Parse_NullConditionalGet_ProducesNullConditionalGetExpr()
  {
    var program = Parse("obj?.name;");

    var stmtDecl = Assert.IsType<StatementDecl>(Assert.Single(program.Declarations));
    var exprStmt = Assert.IsType<ExpressionStmt>(stmtDecl.Statement);
    var nullCond = Assert.IsType<NullConditionalGetExpr>(exprStmt.Expression);

    Assert.IsType<IdentifierExpr>(nullCond.Target);
    Assert.Equal("name", nullCond.Name.Lexeme);
  }

  [Fact]
  public void Parse_NullConditionalCall_WrapsCalleeInNullConditionalGetExpr()
  {
    // "obj?.greet()" — the callee should be a NullConditionalGetExpr, not a GetExpr.
    var program = Parse("obj?.greet();");

    var stmtDecl = Assert.IsType<StatementDecl>(Assert.Single(program.Declarations));
    var exprStmt = Assert.IsType<ExpressionStmt>(stmtDecl.Statement);
    var call = Assert.IsType<CallExpr>(exprStmt.Expression);
    Assert.IsType<NullConditionalGetExpr>(call.Callee);
  }
}
