using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Common.Token;

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
}
