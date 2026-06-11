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
/// Step 5 coverage for the cursor-rewrite plan (docs:
/// validated-nibbling-narwhal): <see cref="ReturnStmt"/>, <see cref="ThrowStmt"/>,
/// and <see cref="TryStmt"/>, including catch and finally, all evaluated
/// synchronously via <see cref="EvaluationCursor.Execute"/> with no suspension
/// possible yet.
/// </summary>
public class CursorTryThrowReturnExecutorTests
{
  private static EvaluationCursor MakeCursor() => new EvaluationCursor(new FunctionValueFactory());

  private static StepResult Execute(EvaluationCursor cursor, Stmt statement, ScriptEnvironment environment)
  {
    var step = cursor.Execute(statement, environment);
    Assert.False(step.IsAwaiting);
    return step;
  }

  private static long GetInt(ScriptEnvironment environment, string name)
  {
    Assert.True(environment.TryGet(name, out var value));
    return Assert.IsType<IntValue>(value).Value;
  }

  [Fact]
  public void Execute_Return_SetsReturnSignalWithItsValue()
  {
    var cursor = MakeCursor();
    var environment = new ScriptEnvironment();

    Execute(cursor, new ReturnStmt(Nodes.Token(ALKScriptTokenType.Return, "return"), Nodes.Literal(42L)), environment);

    Assert.NotNull(cursor.Signal);
    Assert.Equal(SignalKind.Return, cursor.Signal!.Value.Kind);
    Assert.Equal(42L, Assert.IsType<IntValue>(cursor.Signal.Value.Value).Value);
  }

  [Fact]
  public void Execute_Throw_SetsThrownSignalWithItsValue()
  {
    var cursor = MakeCursor();
    var environment = new ScriptEnvironment();

    Execute(cursor, new ThrowStmt(Nodes.Token(ALKScriptTokenType.Throw, "throw"), Nodes.Literal("boom")), environment);

    Assert.NotNull(cursor.Signal);
    Assert.Equal(SignalKind.Thrown, cursor.Signal!.Value.Kind);
    Assert.Equal("boom", Assert.IsType<StringValue>(cursor.Signal.Value.Value).Value);
  }

  [Fact]
  public void Execute_TryCatch_HandlesAThrownValueAndClearsTheSignal()
  {
    var cursor = MakeCursor();
    var environment = new ScriptEnvironment();
    environment.Define("result", new IntValue(0));

    var tryBlock = new BlockStmt(new List<Stmt>
    {
      new ThrowStmt(Nodes.Token(ALKScriptTokenType.Throw, "throw"), Nodes.Literal(7L)),
    });

    var catchBlock = new BlockStmt(new List<Stmt>
    {
      new ExpressionStmt(new AssignmentExpr(Nodes.Ident("result"), Nodes.Ident("e"))),
    });

    var tryStmt = new TryStmt(tryBlock, new[] { new CatchClause(exceptionType: null, Nodes.Identifier("e"), catchBlock) }, finallyBlock: null);

    Execute(cursor, tryStmt, environment);

    Assert.Null(cursor.Signal);
    Assert.Equal(7L, GetInt(environment, "result"));
  }

  [Fact]
  public void Execute_TryFinally_RunsFinallyEvenWhenNoExceptionIsThrown()
  {
    var cursor = MakeCursor();
    var environment = new ScriptEnvironment();
    environment.Define("ranFinally", BoolValue.Of(false));

    var tryBlock = new BlockStmt(new List<Stmt>());
    var finallyBlock = new BlockStmt(new List<Stmt>
    {
      new ExpressionStmt(new AssignmentExpr(Nodes.Ident("ranFinally"), Nodes.Literal(true))),
    });

    var tryStmt = new TryStmt(tryBlock, System.Array.Empty<CatchClause>(), finallyBlock);

    Execute(cursor, tryStmt, environment);

    Assert.Null(cursor.Signal);
    Assert.True(environment.TryGet("ranFinally", out var value));
    Assert.True(Assert.IsType<BoolValue>(value).Value);
  }

  [Fact]
  public void Execute_TryFinally_PropagatesAnUnhandledThrowAfterFinally()
  {
    var cursor = MakeCursor();
    var environment = new ScriptEnvironment();
    environment.Define("ranFinally", BoolValue.Of(false));

    var tryBlock = new BlockStmt(new List<Stmt>
    {
      new ThrowStmt(Nodes.Token(ALKScriptTokenType.Throw, "throw"), Nodes.Literal(99L)),
    });

    var finallyBlock = new BlockStmt(new List<Stmt>
    {
      new ExpressionStmt(new AssignmentExpr(Nodes.Ident("ranFinally"), Nodes.Literal(true))),
    });

    var tryStmt = new TryStmt(tryBlock, System.Array.Empty<CatchClause>(), finallyBlock);

    Execute(cursor, tryStmt, environment);

    Assert.True(environment.TryGet("ranFinally", out var value));
    Assert.True(Assert.IsType<BoolValue>(value).Value);

    Assert.NotNull(cursor.Signal);
    Assert.Equal(SignalKind.Thrown, cursor.Signal!.Value.Kind);
    Assert.Equal(99L, Assert.IsType<IntValue>(cursor.Signal.Value.Value).Value);
  }
}
