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
}
