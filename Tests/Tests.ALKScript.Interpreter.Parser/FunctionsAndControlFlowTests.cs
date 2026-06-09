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
    Assert.False(function.IsAsync);
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
    Assert.False(function.IsAsync);
    Assert.Equal("print", function.Name.Lexeme);
    Assert.Null(function.Body);
  }

  [Fact]
  public void Parse_NativeAsyncFunctionDeclaration_CapturesBothModifiers()
  {
    var program = Parse("native async function string fetch(string url);");

    var function = Assert.IsType<FunctionDecl>(Assert.Single(program.Declarations));
    Assert.True(function.IsNative);
    Assert.True(function.IsAsync);
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

  // ── async-requires-await rule ─────────────────────────────────────────────

  [Fact]
  public void Parse_AsyncFunctionWithAwaitInBody_IsValid()
  {
    // A non-native function may declare itself 'async' if (and only if) its
    // body contains at least one 'await' expression.
    var program = Parse("async function void run() { await fetchValue(); }");

    var function = Assert.IsType<FunctionDecl>(Assert.Single(program.Declarations));
    Assert.True(function.IsAsync);
    Assert.False(function.IsNative);
  }

  [Fact]
  public void Parse_AsyncFunctionWithoutAwaitInBody_ThrowsParseException()
  {
    // A plain (non-native, non-abstract) function that declares itself 'async'
    // but never awaits anything is rejected — it would run synchronously, so
    // the 'async' modifier serves no purpose.
    var exception = Assert.Throws<ParseException>(() =>
      Parse("async function string process(string name) { return name; }"));

    Assert.Contains("'async' is only valid on functions whose body contains at least one 'await' expression", exception.Message);
  }

  [Fact]
  public void Parse_NativeAsyncFunction_IsValidWithoutAwaitInBody()
  {
    // Native async functions are exempt from the "async requires await" rule
    // because the host provides the (async) implementation; there is no body
    // to check.
    var program = Parse("native async function string fetch(string url);");

    var function = Assert.IsType<FunctionDecl>(Assert.Single(program.Declarations));
    Assert.True(function.IsNative);
    Assert.True(function.IsAsync);
  }

  [Fact]
  public void Parse_AsyncMethodWithAwaitInBody_IsValid()
  {
    var program = Parse(
      "class Loader {\n" +
      "  async function string load() { return await fetch(); }\n" +
      "}");

    var classDecl = Assert.IsType<ClassDecl>(Assert.Single(program.Declarations));
    var method = Assert.IsType<MethodDecl>(Assert.Single(classDecl.Members));
    Assert.True(method.IsAsync);
  }

  [Fact]
  public void Parse_AsyncMethodWithoutAwaitInBody_ThrowsParseException()
  {
    var exception = Assert.Throws<ParseException>(() => Parse(
      "class Transformer {\n" +
      "  async function string transform(string s) { return s; }\n" +
      "}"));

    Assert.Contains("'async' is only valid on methods whose body contains at least one 'await' expression", exception.Message);
  }

  [Fact]
  public void Parse_AbstractAsyncMethod_IsValidWithoutAwaitInBody()
  {
    // Abstract methods are exempt: the body (and the awaits it must contain)
    // are deferred to the concrete override.
    var program = Parse(
      "abstract class Loader {\n" +
      "  public abstract async function string load(string url);\n" +
      "}");

    var classDecl = Assert.IsType<ClassDecl>(Assert.Single(program.Declarations));
    var method = Assert.IsType<MethodDecl>(Assert.Single(classDecl.Members));
    Assert.Equal(OverrideModifier.Abstract, method.OverrideModifier);
    Assert.True(method.IsAsync);
  }

  [Fact]
  public void Parse_NativeAsyncMethod_IsValidWithoutAwaitInBody()
  {
    // Native methods are exempt: the host provides the async implementation.
    var program = Parse(
      "native class HttpClient {\n" +
      "  public native async function string get(string url);\n" +
      "}");

    var classDecl = Assert.IsType<ClassDecl>(Assert.Single(program.Declarations));
    var method = Assert.IsType<MethodDecl>(Assert.Single(classDecl.Members));
    Assert.True(method.IsNative);
    Assert.True(method.IsAsync);
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
