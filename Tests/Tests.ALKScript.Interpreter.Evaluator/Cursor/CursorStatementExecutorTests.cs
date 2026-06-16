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
/// Step 2 coverage for the cursor-rewrite plan (docs:
/// validated-nibbling-narwhal): <see cref="ExpressionStmt"/>,
/// <see cref="VariableDecl"/>, <see cref="BlockStmt"/>, and
/// <see cref="IfStmt"/>, all evaluated synchronously via
/// <see cref="EvaluationCursor.Execute"/> with no suspension possible yet.
/// </summary>
public class CursorStatementExecutorTests
{
  private static EvaluationCursor MakeCursor() => new EvaluationCursor(new FunctionValueFactory());

  private static void ExecuteCompleted(EvaluationCursor cursor, Stmt statement, ScriptEnvironment environment)
  {
    var step = cursor.Execute(statement, environment);
    Assert.False(step.IsAwaiting);
  }

  [Fact]
  public void Execute_VariableDecl_DefinesTheNameWithItsInitializerValue()
  {
    var cursor = MakeCursor();
    var environment = new ScriptEnvironment();

    ExecuteCompleted(cursor, new VariableDecl(type: null, Nodes.Identifier("x"), Nodes.Literal(5L)), environment);

    Assert.True(environment.TryGet("x", out var value));
    Assert.Equal(5L, Assert.IsType<IntValue>(value).Value);
  }

  [Fact]
  public void Execute_VariableDecl_WithoutInitializer_DefinesNull()
  {
    var cursor = MakeCursor();
    var environment = new ScriptEnvironment();

    ExecuteCompleted(cursor, new VariableDecl(type: null, Nodes.Identifier("x"), initializer: null), environment);

    Assert.True(environment.TryGet("x", out var value));
    Assert.Same(NullValue.Instance, value);
  }

  [Fact]
  public void Execute_ExpressionStmt_EvaluatesItsExpression()
  {
    var cursor = MakeCursor();
    var environment = new ScriptEnvironment();

    // "x = 1 + 2" via an AssignmentExpr would hit the not-yet-supported path,
    // so exercise something within Step 1's coverage instead: a binary
    // expression with no observable side effect beyond not throwing.
    var expression = new BinaryExpr(Nodes.Literal(1L), Nodes.Operator(ALKScriptTokenType.Plus, "+"), Nodes.Literal(2L));

    ExecuteCompleted(cursor, new ExpressionStmt(expression), environment);
  }

  [Fact]
  public void Execute_BlockStmt_RunsStatementsInANewChildScope()
  {
    var cursor = MakeCursor();
    var environment = new ScriptEnvironment();
    environment.Define("x", new IntValue(1));

    var block = new BlockStmt(new List<Stmt>
    {
      new VariableDecl(type: null, Nodes.Identifier("x"), Nodes.Literal(2L)),
    });

    ExecuteCompleted(cursor, block, environment);

    // The block's "var x = 2" shadows in a child scope — the outer binding is unchanged.
    Assert.True(environment.TryGet("x", out var value));
    Assert.Equal(1L, Assert.IsType<IntValue>(value).Value);
  }

  [Fact]
  public void Execute_IfStmt_ExecutesThenBranchWhenTruthy()
  {
    var cursor = MakeCursor();
    var environment = new ScriptEnvironment();

    var ifStmt = new IfStmt(
      Nodes.Literal(true),
      new VariableDecl(type: null, Nodes.Identifier("x"), Nodes.Literal(1L)),
      new VariableDecl(type: null, Nodes.Identifier("x"), Nodes.Literal(2L)));

    ExecuteCompleted(cursor, ifStmt, environment);

    Assert.True(environment.TryGet("x", out var value));
    Assert.Equal(1L, Assert.IsType<IntValue>(value).Value);
  }

  [Fact]
  public void Execute_IfStmt_ExecutesElseBranchWhenFalsy()
  {
    var cursor = MakeCursor();
    var environment = new ScriptEnvironment();

    var ifStmt = new IfStmt(
      Nodes.Literal(false),
      new VariableDecl(type: null, Nodes.Identifier("x"), Nodes.Literal(1L)),
      new VariableDecl(type: null, Nodes.Identifier("x"), Nodes.Literal(2L)));

    ExecuteCompleted(cursor, ifStmt, environment);

    Assert.True(environment.TryGet("x", out var value));
    Assert.Equal(2L, Assert.IsType<IntValue>(value).Value);
  }

  [Fact]
  public void Execute_VariableDecl_DoesNotDefineWhenInitializerRaisesASignal()
  {
    var cursor = MakeCursor();
    var environment = new ScriptEnvironment();

    var initializer = new ThrowStmt(Nodes.Token(ALKScriptTokenType.Throw, "throw"), Nodes.Literal(1L));
    var block = new BlockStmt(new List<Stmt>
    {
      initializer,
      new VariableDecl(type: null, Nodes.Identifier("x"), Nodes.Literal(1L)),
    });

    ExecuteCompleted(cursor, block, environment);

    Assert.False(environment.TryGet("x", out _));
  }

  [Fact]
  public void Execute_ReturnStatementWithoutValue_SetsReturnSignalToNull()
  {
    var cursor = MakeCursor();
    var environment = new ScriptEnvironment();

    ExecuteCompleted(cursor, new ReturnStmt(Nodes.Token(ALKScriptTokenType.Return, "return"), value: null), environment);

    Assert.NotNull(cursor.Signal);
    Assert.Equal(SignalKind.Return, cursor.Signal!.Value.Kind);
    Assert.Same(NullValue.Instance, cursor.Signal.Value.Value);
  }

  [Fact]
  public void Execute_AlreadyPendingSignal_SkipsExecutionEntirely()
  {
    var cursor = MakeCursor();
    var environment = new ScriptEnvironment();
    environment.Define("evaluated", BoolValue.Of(false));

    var block = new BlockStmt(new List<Stmt>
    {
      new ReturnStmt(Nodes.Token(ALKScriptTokenType.Return, "return"), Nodes.Literal(1L)),
      new ExpressionStmt(new AssignmentExpr(Nodes.Ident("evaluated"), Nodes.Literal(true))),
    });

    ExecuteCompleted(cursor, block, environment);

    Assert.True(environment.TryGet("evaluated", out var value));
    Assert.False(Assert.IsType<BoolValue>(value).Value);
  }

  [Fact]
  public void Execute_FunctionDecl_DefinesItsValueViaTheFunctionValueFactory()
  {
    var cursor = MakeCursor();
    var environment = new ScriptEnvironment();

    var declaration = new FunctionDecl(false, System.Array.Empty<string>(), Nodes.VoidType, Nodes.Identifier("greet"), System.Array.Empty<Parameter>(), new BlockStmt(System.Array.Empty<Stmt>()));

    ExecuteCompleted(cursor, declaration, environment);

    Assert.True(environment.TryGet("greet", out var value));
    var function = Assert.IsType<FunctionValue>(value);
    Assert.Same(declaration, function.Declaration);
  }

  [Fact]
  public void Execute_ClassDeclWithSuperclassNameThatIsNotAClass_ThrowsRuntimeException()
  {
    var cursor = MakeCursor();
    var environment = new ScriptEnvironment();
    environment.Define("Missing", new IntValue(1));

    var declaration = new ClassDecl(
      false,
      Nodes.Identifier("Derived"),
      System.Array.Empty<string>(),
      superclassName: Nodes.Identifier("Missing"),
      superclassTypeArguments: System.Array.Empty<TypeNode>(),
      members: System.Array.Empty<MemberDecl>());

    var exception = Assert.Throws<RuntimeException>(() => cursor.Execute(declaration, environment));

    Assert.Contains("'Missing' is not a class", exception.Message);
  }

  [Fact]
  public void Execute_DuplicateFunctionDecl_ThrowsRuntimeException()
  {
    var cursor = MakeCursor();
    var environment = new ScriptEnvironment();
    var body = new BlockStmt(System.Array.Empty<Stmt>());
    var first = new FunctionDecl(false, System.Array.Empty<string>(), Nodes.VoidType, Nodes.Identifier("foo"), System.Array.Empty<Parameter>(), body);
    var second = new FunctionDecl(false, System.Array.Empty<string>(), Nodes.VoidType, Nodes.Identifier("foo"), System.Array.Empty<Parameter>(), body);

    cursor.Execute(first, environment);
    var exception = Assert.Throws<RuntimeException>(() => cursor.Execute(second, environment));

    Assert.Contains("already defined in this scope", exception.Message);
    Assert.Contains("'foo'", exception.Message);
  }

  [Fact]
  public void Execute_DuplicateFunctionDeclInEnclosingScope_Allowed()
  {
    var cursor = MakeCursor();
    var outer = new ScriptEnvironment();
    var body = new BlockStmt(System.Array.Empty<Stmt>());
    cursor.Execute(new FunctionDecl(false, System.Array.Empty<string>(), Nodes.VoidType, Nodes.Identifier("foo"), System.Array.Empty<Parameter>(), body), outer);

    // Declaring 'foo' again in an inner scope shadows the outer one — allowed.
    var inner = new ScriptEnvironment(outer);
    cursor.Execute(new FunctionDecl(false, System.Array.Empty<string>(), Nodes.VoidType, Nodes.Identifier("foo"), System.Array.Empty<Parameter>(), body), inner);

    Assert.True(inner.TryGet("foo", out _));
  }
}
