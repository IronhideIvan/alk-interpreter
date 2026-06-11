using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Common.Token;
using ALKScript.Interpreter.Parser;

namespace Tests.ALKScript.Interpreter.Parser;

public class LambdaTests : ParserTestBase
{
  [Fact]
  public void Parse_LambdaExpression_ProducesLambdaExprWithParametersAndReturnType()
  {
    var program = Parse("var add = int (int x, int y) => { return x + y; };");

    var decl = Assert.IsType<VariableDecl>(Assert.Single(program.Declarations));
    var lambda = Assert.IsType<LambdaExpr>(decl.Initializer);

    Assert.Equal("int", lambda.ReturnType.Name);
    Assert.Equal(2, lambda.Parameters.Count);
    Assert.Equal("int", lambda.Parameters[0].Type.Name);
    Assert.Equal("x", lambda.Parameters[0].Name);
    Assert.Equal("int", lambda.Parameters[1].Type.Name);
    Assert.Equal("y", lambda.Parameters[1].Name);

    var stmtDecl = Assert.IsType<StatementDecl>(Assert.Single(lambda.Body.Statements));
    var returnStmt = Assert.IsType<ReturnStmt>(stmtDecl.Statement);
    Assert.IsType<BinaryExpr>(returnStmt.Value);
  }

  [Fact]
  public void Parse_ZeroParameterLambda_ProducesEmptyParameters()
  {
    var program = Parse("var f = void () => { log(\"hi\"); };");

    var decl = Assert.IsType<VariableDecl>(Assert.Single(program.Declarations));
    var lambda = Assert.IsType<LambdaExpr>(decl.Initializer);

    Assert.Equal("void", lambda.ReturnType.Name);
    Assert.Empty(lambda.Parameters);
  }

  [Fact]
  public void Parse_LambdaTypeAnnotation_CapturesReturnAndParameterTypesAsTypeArguments()
  {
    var program = Parse("lambda<int, int, int> add = int (int x, int y) => { return x + y; };");

    var decl = Assert.IsType<VariableDecl>(Assert.Single(program.Declarations));

    Assert.NotNull(decl.Type);
    Assert.Equal("lambda", decl.Type!.Name);
    Assert.Equal(3, decl.Type.TypeArguments.Count);
    Assert.Equal("int", decl.Type.TypeArguments[0].Name);
    Assert.Equal("int", decl.Type.TypeArguments[1].Name);
    Assert.Equal("int", decl.Type.TypeArguments[2].Name);

    Assert.IsType<LambdaExpr>(decl.Initializer);
  }

  [Fact]
  public void Parse_BareLambdaTypeAnnotation_IsEquivalentToLambdaOfVoid()
  {
    var program = Parse("lambda action = void () => { };");

    var decl = Assert.IsType<VariableDecl>(Assert.Single(program.Declarations));

    Assert.NotNull(decl.Type);
    Assert.Equal("lambda", decl.Type!.Name);
    Assert.Equal("void", Assert.Single(decl.Type.TypeArguments).Name);
  }

  [Fact]
  public void Parse_LambdaPassedAsCallArgument_ProducesLambdaExprArgument()
  {
    var program = Parse("forEach(void (Item it) => { log(it); });");

    var stmtDecl = Assert.IsType<StatementDecl>(Assert.Single(program.Declarations));
    var exprStmt = Assert.IsType<ExpressionStmt>(stmtDecl.Statement);
    var call = Assert.IsType<CallExpr>(exprStmt.Expression);

    var lambda = Assert.IsType<LambdaExpr>(Assert.Single(call.Arguments));
    Assert.Equal("void", lambda.ReturnType.Name);
    Assert.Equal("Item", lambda.Parameters[0].Type.Name);
    Assert.Equal("it", lambda.Parameters[0].Name);
  }

  [Fact]
  public void Parse_LambdaWithAwaitInBody_IsValid()
  {
    var program = Parse("var f = int (int x) => { return await fetchValue(x); };");

    var decl = Assert.IsType<VariableDecl>(Assert.Single(program.Declarations));
    var lambda = Assert.IsType<LambdaExpr>(decl.Initializer);

    Assert.Equal("int", lambda.ReturnType.Name);
  }

  [Fact]
  public void Parse_LambdaWithThunkReturnType_IsValid()
  {
    var program = Parse("var f = thunk<int> (int x) => { return fetchValue(x); };");

    var decl = Assert.IsType<VariableDecl>(Assert.Single(program.Declarations));
    var lambda = Assert.IsType<LambdaExpr>(decl.Initializer);

    Assert.Equal("thunk", lambda.ReturnType.Name);
  }

  [Fact]
  public void Parse_CallExpressionThatLooksLikeLambdaPrefix_StillParsesAsCall()
  {
    // "Calculate(value)" begins like a lambda return type ("Calculate") followed
    // by "(", but "value" alone isn't a "type identifier" parameter, so this
    // must back off to an ordinary call expression.
    var program = Parse("var result = Calculate(value);");

    var decl = Assert.IsType<VariableDecl>(Assert.Single(program.Declarations));
    var call = Assert.IsType<CallExpr>(decl.Initializer);
    var callee = Assert.IsType<IdentifierExpr>(call.Callee);
    Assert.Equal("Calculate", callee.Name.Lexeme);
  }

  [Fact]
  public void Parse_ComparisonExpressionThatLooksLikeGenericLambdaPrefix_StillParsesAsComparison()
  {
    // "a < b > c" must still parse as "(a < b) > c", not be mistaken for a
    // lambda whose return type is the generic "a<b>".
    var program = Parse("var flag = a < b > c;");

    var decl = Assert.IsType<VariableDecl>(Assert.Single(program.Declarations));
    var outer = Assert.IsType<BinaryExpr>(decl.Initializer);
    Assert.Equal(ALKScriptTokenType.Greater, outer.Operator.Type);
    Assert.IsType<BinaryExpr>(outer.Left);
  }
}
