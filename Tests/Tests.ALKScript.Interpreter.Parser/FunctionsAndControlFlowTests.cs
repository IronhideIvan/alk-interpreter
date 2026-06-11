using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Parser;

namespace Tests.ALKScript.Interpreter.Parser;

public class FunctionsAndControlFlowTests : ParserTestBase
{
  [Fact]
  public void Parse_FunctionDeclarationWithReturnType_ProducesFunctionDecl()
  {
    var program = Parse("function int add(int a, int b) {\n  return a + b;\n}");

    var function = Assert.IsType<FunctionDecl>(Assert.Single(program.Declarations));
    Assert.Equal("int", function.ReturnType.Name);
    Assert.Equal("add", function.Name.Lexeme);
    Assert.Equal(2, function.Parameters.Count);
    Assert.Equal("a", function.Parameters[0].Name);
    Assert.Equal("int", function.Parameters[0].Type.Name);

    Assert.NotNull(function.Body);
    var stmtDecl = Assert.IsType<StatementDecl>(Assert.Single(function.Body!.Statements));
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
    Assert.NotNull(function.Body);
    Assert.Equal(2, function.Body!.Statements.Count);

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
  public void Parse_NativeFunctionDeclaration_HasNoBodyAndIsFlaggedNative()
  {
    var program = Parse("native function void print(string message);");

    var function = Assert.IsType<FunctionDecl>(Assert.Single(program.Declarations));
    Assert.True(function.IsNative);
    Assert.Equal("print", function.Name.Lexeme);
    Assert.Null(function.Body);
  }

  [Fact]
  public void Parse_NativeThunkFunctionDeclaration_CapturesThunkReturnType()
  {
    var program = Parse("native function thunk<string> fetch(string url);");

    var function = Assert.IsType<FunctionDecl>(Assert.Single(program.Declarations));
    Assert.True(function.IsNative);
    Assert.Equal("thunk", function.ReturnType.Name);
    Assert.Null(function.Body);
  }

  [Fact]
  public void Parse_NativeFunctionDeclarationWithBody_ThrowsParseException()
  {
    Assert.Throws<ParseException>(() => Parse("native function void print(string message) {}"));
  }

  [Fact]
  public void Parse_NativeMethodDeclaration_HasNoBodyAndIsFlaggedNative()
  {
    var program = Parse("native class Console {\n  public native function void log(string message);\n}");

    var classDecl = Assert.IsType<ClassDecl>(Assert.Single(program.Declarations));
    var method = Assert.IsType<MethodDecl>(Assert.Single(classDecl.Members));

    Assert.True(classDecl.IsNative);
    Assert.True(method.IsNative);
    Assert.Equal(AccessModifier.Public, method.AccessModifier);
    Assert.Equal("log", method.Name.Lexeme);
    Assert.Null(method.Body);
  }

  [Fact]
  public void Parse_NativeClassDeclaration_IsFlaggedNative()
  {
    var program = Parse("native class Console {\n  public native function void log(string message);\n}");

    var classDecl = Assert.IsType<ClassDecl>(Assert.Single(program.Declarations));
    Assert.True(classDecl.IsNative);
  }

  [Fact]
  public void Parse_NativeAbstractClassDeclaration_CapturesBothModifiers()
  {
    var program = Parse("native abstract class Console {\n  public native function void log(string message);\n}");

    var classDecl = Assert.IsType<ClassDecl>(Assert.Single(program.Declarations));
    Assert.True(classDecl.IsNative);
    Assert.True(classDecl.IsAbstract);
  }

  [Fact]
  public void Parse_ClassWithNativeMemberButWithoutNativeKeyword_ThrowsParseException()
  {
    var exception = Assert.Throws<ParseException>(() =>
      Parse("class Console {\n  public native function void log(string message);\n}"));

    Assert.Contains("must be declared 'native'", exception.Message);
  }

  [Fact]
  public void Parse_NativeClassWithoutNativeMembers_ParsesSuccessfully()
  {
    var program = Parse("native class Marker {\n}");

    var classDecl = Assert.IsType<ClassDecl>(Assert.Single(program.Declarations));
    Assert.True(classDecl.IsNative);
    Assert.Empty(classDecl.Members);
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

  // ── thunk return type ─────────────────────────────────────────────────────

  [Fact]
  public void Parse_FunctionWithAwaitInBody_IsValid()
  {
    // 'await' is a universally-valid operator, usable in any function body.
    var program = Parse("function void run() { await fetchValue(); }");

    var function = Assert.IsType<FunctionDecl>(Assert.Single(program.Declarations));
    Assert.False(function.IsNative);
  }

  [Fact]
  public void Parse_MethodWithThunkReturnTypeAndAwaitInBody_IsValid()
  {
    var program = Parse(
      "class Loader {\n" +
      "  function thunk<string> load() { return await fetch(); }\n" +
      "}");

    var classDecl = Assert.IsType<ClassDecl>(Assert.Single(program.Declarations));
    var method = Assert.IsType<MethodDecl>(Assert.Single(classDecl.Members));
    Assert.Equal("thunk", method.ReturnType.Name);
  }

  [Fact]
  public void Parse_AbstractMethodWithThunkReturnType_IsValidWithoutBody()
  {
    var program = Parse(
      "abstract class Loader {\n" +
      "  public abstract function thunk<string> load(string url);\n" +
      "}");

    var classDecl = Assert.IsType<ClassDecl>(Assert.Single(program.Declarations));
    var method = Assert.IsType<MethodDecl>(Assert.Single(classDecl.Members));
    Assert.Equal(OverrideModifier.Abstract, method.OverrideModifier);
    Assert.Equal("thunk", method.ReturnType.Name);
  }

  [Fact]
  public void Parse_NativeMethodWithThunkReturnType_IsValidWithoutBody()
  {
    var program = Parse(
      "native class HttpClient {\n" +
      "  public native function thunk<string> get(string url);\n" +
      "}");

    var classDecl = Assert.IsType<ClassDecl>(Assert.Single(program.Declarations));
    var method = Assert.IsType<MethodDecl>(Assert.Single(classDecl.Members));
    Assert.True(method.IsNative);
    Assert.Equal("thunk", method.ReturnType.Name);
  }

  // ── foreach ───────────────────────────────────────────────────────────────

  [Fact]
  public void Parse_ForeachStatement_ProducesForeachStmtWithVariableAndCollection()
  {
    var program = Parse("foreach (var item in items) { }");

    var stmtDecl = Assert.IsType<StatementDecl>(Assert.Single(program.Declarations));
    var forEach = Assert.IsType<ForeachStmt>(stmtDecl.Statement);

    Assert.Equal("item",  forEach.Variable.Lexeme);
    Assert.Equal("foreach", forEach.Keyword.Lexeme);
    Assert.IsType<IdentifierExpr>(forEach.Collection);
    Assert.IsType<BlockStmt>(forEach.Body);
  }

  // ── do-while ─────────────────────────────────────────────────────────────

  [Fact]
  public void Parse_DoWhileStatement_ProducesDoWhileStmtWithBodyAndCondition()
  {
    var program = Parse("do { } while (true);");

    var stmtDecl = Assert.IsType<StatementDecl>(Assert.Single(program.Declarations));
    var doWhile = Assert.IsType<DoWhileStmt>(stmtDecl.Statement);

    Assert.Equal("do", doWhile.Keyword.Lexeme);
    Assert.IsType<BlockStmt>(doWhile.Body);
    Assert.IsType<LiteralExpr>(doWhile.Condition);
  }
}
