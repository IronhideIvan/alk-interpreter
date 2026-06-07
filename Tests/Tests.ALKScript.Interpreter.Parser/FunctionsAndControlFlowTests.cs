using ALKScript.Interpreter.Common.Ast;

namespace Tests.ALKScript.Interpreter.Parser;

public class FunctionsAndControlFlowTests : ParserTestBase
{
  [Fact]
  public void Parse_FunctionDeclarationWithReturnType_ProducesFunctionDecl()
  {
    var program = Parse("function int add(int a, int b) {\n  return a + b;\n}");

    var function = Assert.IsType<FunctionDecl>(Assert.Single(program.Declarations));
    Assert.False(function.IsAsync);
    Assert.Equal("int", function.ReturnType.Name);
    Assert.Equal("add", function.Name.Lexeme);
    Assert.Equal(2, function.Parameters.Count);
    Assert.Equal("a", function.Parameters[0].Name);
    Assert.Equal("int", function.Parameters[0].Type.Name);

    var stmtDecl = Assert.IsType<StatementDecl>(Assert.Single(function.Body.Statements));
    var returnStmt = Assert.IsType<ReturnStmt>(stmtDecl.Statement);
    Assert.NotNull(returnStmt.Value);
    Assert.IsType<BinaryExpr>(returnStmt.Value);
  }

  [Fact]
  public void Parse_VoidFunctionDeclaration_AllowsEmptyReturn()
  {
    var program = Parse("function void log(string message) {\n  print(message);\n  return;\n}");

    var function = Assert.IsType<FunctionDecl>(Assert.Single(program.Declarations));
    Assert.Equal("void", function.ReturnType.Name);
    Assert.Equal(2, function.Body.Statements.Count);

    var stmtDecl = Assert.IsType<StatementDecl>(function.Body.Statements[1]);
    var returnStmt = Assert.IsType<ReturnStmt>(stmtDecl.Statement);
    Assert.Null(returnStmt.Value);
  }

  [Fact]
  public void Parse_GenericFunctionDeclaration_CapturesTypeParameters()
  {
    var program = Parse("function<T> void process(T n) {\n}");

    var function = Assert.IsType<FunctionDecl>(Assert.Single(program.Declarations));
    var typeParameter = Assert.Single(function.TypeParameters);
    Assert.Equal("T", typeParameter);
    Assert.Equal("T", function.Parameters[0].Type.Name);
  }

  [Fact]
  public void Parse_IfElseStatement_ProducesIfStmtWithBothBranches()
  {
    var program = Parse("if (a < b) { return a; } else { return b; }");

    var stmtDecl = Assert.IsType<StatementDecl>(Assert.Single(program.Declarations));
    var ifStmt = Assert.IsType<IfStmt>(stmtDecl.Statement);

    Assert.IsType<BinaryExpr>(ifStmt.Condition);
    Assert.IsType<BlockStmt>(ifStmt.ThenBranch);
    Assert.NotNull(ifStmt.ElseBranch);
    Assert.IsType<BlockStmt>(ifStmt.ElseBranch);
  }

  [Fact]
  public void Parse_WhileStatement_ProducesWhileStmt()
  {
    var program = Parse("while (x < 10) { x = x + 1; }");

    var stmtDecl = Assert.IsType<StatementDecl>(Assert.Single(program.Declarations));
    var whileStmt = Assert.IsType<WhileStmt>(stmtDecl.Statement);

    Assert.IsType<BinaryExpr>(whileStmt.Condition);
    Assert.IsType<BlockStmt>(whileStmt.Body);
  }

  [Fact]
  public void Parse_ForStatement_CapturesAllThreeClauses()
  {
    var program = Parse("for (var i = 0; i < 10; i = i + 1) { }");

    var stmtDecl = Assert.IsType<StatementDecl>(Assert.Single(program.Declarations));
    var forStmt = Assert.IsType<ForStmt>(stmtDecl.Statement);

    Assert.IsType<VariableDecl>(forStmt.Initializer);
    Assert.IsType<BinaryExpr>(forStmt.Condition);
    Assert.IsType<AssignmentExpr>(forStmt.Increment);
    Assert.IsType<BlockStmt>(forStmt.Body);
  }

  [Fact]
  public void Parse_ForStatementWithOmittedClauses_AllowsNullClauses()
  {
    var program = Parse("for (;;) { }");

    var stmtDecl = Assert.IsType<StatementDecl>(Assert.Single(program.Declarations));
    var forStmt = Assert.IsType<ForStmt>(stmtDecl.Statement);

    Assert.Null(forStmt.Initializer);
    Assert.Null(forStmt.Condition);
    Assert.Null(forStmt.Increment);
  }
}
