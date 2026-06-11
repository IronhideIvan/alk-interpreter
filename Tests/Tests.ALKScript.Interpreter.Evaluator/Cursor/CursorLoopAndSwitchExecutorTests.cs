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
/// Step 3 coverage for the cursor-rewrite plan (docs:
/// validated-nibbling-narwhal): <see cref="WhileStmt"/>, <see cref="ForStmt"/>,
/// <see cref="ForeachStmt"/>, <see cref="DoWhileStmt"/>, and
/// <see cref="SwitchStmt"/>, including <c>break</c>/<c>continue</c>, all
/// evaluated synchronously via <see cref="EvaluationCursor.Execute"/> with no
/// suspension possible yet.
/// </summary>
public class CursorLoopAndSwitchExecutorTests
{
  private static EvaluationCursor MakeCursor() => new EvaluationCursor(new FunctionValueFactory());

  private static void ExecuteCompleted(EvaluationCursor cursor, Stmt statement, ScriptEnvironment environment)
  {
    var step = cursor.Execute(statement, environment);
    Assert.False(step.IsAwaiting);
  }

  private static long GetInt(ScriptEnvironment environment, string name)
  {
    Assert.True(environment.TryGet(name, out var value));
    return Assert.IsType<IntValue>(value).Value;
  }

  private static BinaryExpr LessThan(string identifier, long literal) =>
    new BinaryExpr(Nodes.Ident(identifier), Nodes.Operator(ALKScriptTokenType.Less, "<"), Nodes.Literal(literal));

  private static ExpressionStmt IncrementAssign(string identifier) =>
    new ExpressionStmt(new AssignmentExpr(
      Nodes.Ident(identifier),
      new BinaryExpr(Nodes.Ident(identifier), Nodes.Operator(ALKScriptTokenType.Plus, "+"), Nodes.Literal(1L))));

  [Fact]
  public void Execute_WhileStmt_LoopsUntilConditionIsFalse()
  {
    var cursor = MakeCursor();
    var environment = new ScriptEnvironment();
    environment.Define("i", new IntValue(0));
    environment.Define("sum", new IntValue(0));

    var body = new BlockStmt(new List<Stmt>
    {
      new ExpressionStmt(new AssignmentExpr(
        Nodes.Ident("sum"),
        new BinaryExpr(Nodes.Ident("sum"), Nodes.Operator(ALKScriptTokenType.Plus, "+"), Nodes.Ident("i")))),
      IncrementAssign("i"),
    });

    ExecuteCompleted(cursor, new WhileStmt(LessThan("i", 5L), body), environment);

    Assert.Equal(5L, GetInt(environment, "i"));
    Assert.Equal(0L + 1 + 2 + 3 + 4, GetInt(environment, "sum"));
  }

  [Fact]
  public void Execute_ForStmt_WithBreak_StopsEarly()
  {
    var cursor = MakeCursor();
    var environment = new ScriptEnvironment();
    environment.Define("lastSeen", new IntValue(-1));

    var body = new BlockStmt(new List<Stmt>
    {
      new ExpressionStmt(new AssignmentExpr(Nodes.Ident("lastSeen"), Nodes.Ident("i"))),
      new IfStmt(
        new BinaryExpr(Nodes.Ident("i"), Nodes.Operator(ALKScriptTokenType.EqualEqual, "=="), Nodes.Literal(3L)),
        new BreakStmt(Nodes.Token(ALKScriptTokenType.Break, "break")),
        elseBranch: null),
    });

    var forStmt = new ForStmt(
      initializer: new VariableDecl(type: null, Nodes.Identifier("i"), Nodes.Literal(0L)),
      condition: LessThan("i", 10L),
      increment: new AssignmentExpr(
        Nodes.Ident("i"),
        new BinaryExpr(Nodes.Ident("i"), Nodes.Operator(ALKScriptTokenType.Plus, "+"), Nodes.Literal(1L))),
      body: body);

    ExecuteCompleted(cursor, forStmt, environment);

    Assert.Equal(3L, GetInt(environment, "lastSeen"));
    Assert.Null(cursor.Signal);
  }

  [Fact]
  public void Execute_ForStmt_WithContinue_SkipsIncrementBody()
  {
    var cursor = MakeCursor();
    var environment = new ScriptEnvironment();
    environment.Define("sum", new IntValue(0));

    var body = new BlockStmt(new List<Stmt>
    {
      new IfStmt(
        new BinaryExpr(
          new BinaryExpr(Nodes.Ident("i"), Nodes.Operator(ALKScriptTokenType.Percent, "%"), Nodes.Literal(2L)),
          Nodes.Operator(ALKScriptTokenType.EqualEqual, "=="),
          Nodes.Literal(0L)),
        new ContinueStmt(Nodes.Token(ALKScriptTokenType.Continue, "continue")),
        elseBranch: null),
      new ExpressionStmt(new AssignmentExpr(
        Nodes.Ident("sum"),
        new BinaryExpr(Nodes.Ident("sum"), Nodes.Operator(ALKScriptTokenType.Plus, "+"), Nodes.Ident("i")))),
    });

    var forStmt = new ForStmt(
      initializer: new VariableDecl(type: null, Nodes.Identifier("i"), Nodes.Literal(0L)),
      condition: LessThan("i", 5L),
      increment: new AssignmentExpr(
        Nodes.Ident("i"),
        new BinaryExpr(Nodes.Ident("i"), Nodes.Operator(ALKScriptTokenType.Plus, "+"), Nodes.Literal(1L))),
      body: body);

    ExecuteCompleted(cursor, forStmt, environment);

    // i = 0,1,2,3,4 — odd values (1,3) added: sum = 4
    Assert.Equal(1L + 3L, GetInt(environment, "sum"));
    Assert.Null(cursor.Signal);
  }

  [Fact]
  public void Execute_ForeachStmt_IteratesArrayItems()
  {
    var cursor = MakeCursor();
    var environment = new ScriptEnvironment();
    environment.Define("arr", new ArrayValue(new List<ALKScriptValue> { new IntValue(1), new IntValue(2), new IntValue(3) }));
    environment.Define("sum", new IntValue(0));

    var body = new ExpressionStmt(new AssignmentExpr(
      Nodes.Ident("sum"),
      new BinaryExpr(Nodes.Ident("sum"), Nodes.Operator(ALKScriptTokenType.Plus, "+"), Nodes.Ident("item"))));

    var foreachStmt = new ForeachStmt(
      Nodes.Token(ALKScriptTokenType.Foreach, "foreach"),
      Nodes.Identifier("item"),
      Nodes.Ident("arr"),
      body);

    ExecuteCompleted(cursor, foreachStmt, environment);

    Assert.Equal(6L, GetInt(environment, "sum"));
  }

  [Fact]
  public void Execute_DoWhileStmt_RunsBodyAtLeastOnce()
  {
    var cursor = MakeCursor();
    var environment = new ScriptEnvironment();
    environment.Define("i", new IntValue(0));

    var body = new BlockStmt(new List<Stmt> { IncrementAssign("i") });

    ExecuteCompleted(cursor, new DoWhileStmt(Nodes.Token(ALKScriptTokenType.Do, "do"), body, Nodes.Literal(false)), environment);

    Assert.Equal(1L, GetInt(environment, "i"));
  }

  [Fact]
  public void Execute_SwitchStmt_FallsThroughUntilBreak()
  {
    var cursor = MakeCursor();
    var environment = new ScriptEnvironment();
    environment.Define("result", new IntValue(0));

    var assignTo = (long value) => new ExpressionStmt(new AssignmentExpr(
      Nodes.Ident("result"),
      Nodes.Literal(value)));

    var cases = new List<SwitchCase>
    {
      new SwitchCase(Nodes.Literal(1L), new List<Stmt> { assignTo(10L) }), // falls through
      new SwitchCase(Nodes.Literal(2L), new List<Stmt> { assignTo(20L), new BreakStmt(Nodes.Token(ALKScriptTokenType.Break, "break")) }),
      new SwitchCase(test: null, new List<Stmt> { assignTo(99L) }), // default
    };

    ExecuteCompleted(cursor, new SwitchStmt(Nodes.Token(ALKScriptTokenType.Switch, "switch"), Nodes.Literal(1L), cases), environment);

    Assert.Equal(20L, GetInt(environment, "result"));
    Assert.Null(cursor.Signal);
  }

  [Fact]
  public void Execute_SwitchStmt_FallsBackToDefault()
  {
    var cursor = MakeCursor();
    var environment = new ScriptEnvironment();
    environment.Define("result", new IntValue(0));

    var assignTo = (long value) => new ExpressionStmt(new AssignmentExpr(
      Nodes.Ident("result"),
      Nodes.Literal(value)));

    var cases = new List<SwitchCase>
    {
      new SwitchCase(Nodes.Literal(1L), new List<Stmt> { assignTo(10L), new BreakStmt(Nodes.Token(ALKScriptTokenType.Break, "break")) }),
      new SwitchCase(test: null, new List<Stmt> { assignTo(99L) }),
    };

    ExecuteCompleted(cursor, new SwitchStmt(Nodes.Token(ALKScriptTokenType.Switch, "switch"), Nodes.Literal(5L), cases), environment);

    Assert.Equal(99L, GetInt(environment, "result"));
  }
}
