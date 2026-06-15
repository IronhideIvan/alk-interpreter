using System.Collections.Generic;
using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Common.Evaluation;
using ALKScript.Interpreter.Common.Evaluation.Scheduling;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Common.Token;
using ALKScript.Interpreter.Evaluator;
using ALKScript.Interpreter.Evaluator.Cursor;
using Tests.ALKScript.Interpreter.Evaluator.Unit;

namespace Tests.ALKScript.Interpreter.Evaluator.Cursor;

/// <summary>
/// Step 6/7 coverage for the cursor-rewrite plan (docs:
/// validated-nibbling-narwhal): single-thunk <see cref="AwaitExpr"/>
/// suspend/resume via <see cref="EvaluationCursor.Run"/>/<see cref="EvaluationCursor.Resume"/>/
/// <see cref="EvaluationCursor.ResumeFaulted"/>, the await-placement
/// restriction of plan §4, and the restricted (synchronous-only) "whenAll" of
/// plan §6.
/// </summary>
public class CursorAwaitExecutorTests
{
  private static EvaluationCursor MakeCursor() => new EvaluationCursor(new FunctionValueFactory());

  private static TypeNode IntType => new TypeNode("int", System.Array.Empty<TypeNode>(), 0, false);

  private static long GetInt(ScriptEnvironment environment, string name)
  {
    Assert.True(environment.TryGet(name, out var value));
    return Assert.IsType<IntValue>(value).Value;
  }

  /// <summary>A not-yet-started pending operation, settled externally via <paramref name="source"/>.</summary>
  private static (PendingOperationValue Pending, PendingSource Source) PendingThunk(TypeNode? elementType = null)
  {
    var source = new PendingSource();
    var pending = new PendingOperationValue(new PendingOperation("test", System.Array.Empty<ALKScriptValue>()), new PendingBinder(source), elementType);
    return (pending, source);
  }

  /// <summary>Mutable settlement cell for a <see cref="PendingBinder"/>.</summary>
  private sealed class PendingSource
  {
    internal OperationStatus Status { get; private set; } = OperationStatus.Pending.Instance;

    internal void SetResult(ALKScriptValue value) => Status = new OperationStatus.Resolved(value);
  }

  private sealed class PendingBinder : IAsyncOperationBinder
  {
    private readonly PendingSource _source;

    internal PendingBinder(PendingSource source) => _source = source;

    public OperationStatus Start(PendingOperation operation) => OperationStatus.Pending.Instance;

    public OperationStatus Poll(PendingOperation operation) => _source.Status;

    public void Discard(PendingOperation operation, System.Action<System.Exception> onFault) { }

    public void OnOperationFaulted(PendingOperation operation, System.Exception fault) { }
  }

  [Fact]
  public void Start_VariableDeclAwaitingUnresolvedThunk_ReturnsAwaiting()
  {
    var cursor = MakeCursor();
    var environment = new ScriptEnvironment();
    var (pending, _) = PendingThunk(IntType);
    environment.Define("pending", pending);

    var statements = new List<Stmt>
    {
      new VariableDecl(type: null, Nodes.Identifier("x"), new AwaitExpr(Nodes.Token(ALKScriptTokenType.Await, "await"), Nodes.Ident("pending"))),
    };

    var result = cursor.Start(statements, environment);

    Assert.Equal(RunResult.Awaiting, result);
    Assert.NotNull(cursor.PendingAwait);
  }

  [Fact]
  public void Resume_VariableDeclAwait_BindsResumedValueAndCompletes()
  {
    var cursor = MakeCursor();
    var environment = new ScriptEnvironment();
    var (pending, _) = PendingThunk(IntType);
    environment.Define("pending", pending);

    var statements = new List<Stmt>
    {
      new VariableDecl(type: null, Nodes.Identifier("x"), new AwaitExpr(Nodes.Token(ALKScriptTokenType.Await, "await"), Nodes.Ident("pending"))),
    };

    Assert.Equal(RunResult.Awaiting, cursor.Start(statements, environment));

    var result = cursor.Resume(new IntValue(42));

    Assert.Equal(RunResult.Completed, result);
    Assert.Equal(42L, GetInt(environment, "x"));
  }

  [Fact]
  public void Resume_AwaitInsideIfThenBranch_FastForwardsIntoThenBranchEnvironment()
  {
    var cursor = MakeCursor();
    var environment = new ScriptEnvironment();
    var (pending, _) = PendingThunk(IntType);
    environment.Define("pending", pending);
    environment.Define("result", new IntValue(0));

    var thenBranch = new BlockStmt(new List<Stmt>
    {
      new VariableDecl(type: null, Nodes.Identifier("x"), new AwaitExpr(Nodes.Token(ALKScriptTokenType.Await, "await"), Nodes.Ident("pending"))),
      new ExpressionStmt(new AssignmentExpr(Nodes.Ident("result"), Nodes.Ident("x"))),
    });

    var statements = new List<Stmt>
    {
      new IfStmt(Nodes.Literal(true), thenBranch, elseBranch: null),
    };

    Assert.Equal(RunResult.Awaiting, cursor.Start(statements, environment));

    var result = cursor.Resume(new IntValue(7));

    Assert.Equal(RunResult.Completed, result);
    Assert.Equal(7L, GetInt(environment, "result"));
  }

  [Fact]
  public void Resume_AwaitInsideWhileLoopSecondIteration_ContinuesFromCorrectIteration()
  {
    var cursor = MakeCursor();
    var environment = new ScriptEnvironment();
    var (pending, _) = PendingThunk(IntType);
    environment.Define("pending", pending);
    environment.Define("i", new IntValue(0));
    environment.Define("result", new IntValue(0));

    // while (i < 2) { if (i == 1) { var x = await pending; result = x; } i = i + 1; }
    var body = new BlockStmt(new List<Stmt>
    {
      new IfStmt(
        new BinaryExpr(Nodes.Ident("i"), Nodes.Operator(ALKScriptTokenType.EqualEqual, "=="), Nodes.Literal(1L)),
        new BlockStmt(new List<Stmt>
        {
          new VariableDecl(type: null, Nodes.Identifier("x"), new AwaitExpr(Nodes.Token(ALKScriptTokenType.Await, "await"), Nodes.Ident("pending"))),
          new ExpressionStmt(new AssignmentExpr(Nodes.Ident("result"), Nodes.Ident("x"))),
        }),
        elseBranch: null),
      new ExpressionStmt(new AssignmentExpr(Nodes.Ident("i"), new BinaryExpr(Nodes.Ident("i"), Nodes.Operator(ALKScriptTokenType.Plus, "+"), Nodes.Literal(1L)))),
    });

    var statements = new List<Stmt>
    {
      new WhileStmt(new BinaryExpr(Nodes.Ident("i"), Nodes.Operator(ALKScriptTokenType.Less, "<"), Nodes.Literal(2L)), body),
    };

    Assert.Equal(RunResult.Awaiting, cursor.Start(statements, environment));
    Assert.Equal(1L, GetInt(environment, "i"));

    var result = cursor.Resume(new IntValue(99));

    Assert.Equal(RunResult.Completed, result);
    Assert.Equal(99L, GetInt(environment, "result"));
    Assert.Equal(2L, GetInt(environment, "i"));
  }

  [Fact]
  public void Resume_AwaitInsideTryFinally_RunsFinallyAfterResumption()
  {
    var cursor = MakeCursor();
    var environment = new ScriptEnvironment();
    var (pending, _) = PendingThunk(IntType);
    environment.Define("pending", pending);
    environment.Define("result", new IntValue(0));
    environment.Define("ranFinally", BoolValue.Of(false));

    var tryBlock = new BlockStmt(new List<Stmt>
    {
      new VariableDecl(type: null, Nodes.Identifier("x"), new AwaitExpr(Nodes.Token(ALKScriptTokenType.Await, "await"), Nodes.Ident("pending"))),
      new ExpressionStmt(new AssignmentExpr(Nodes.Ident("result"), Nodes.Ident("x"))),
    });

    var finallyBlock = new BlockStmt(new List<Stmt>
    {
      new ExpressionStmt(new AssignmentExpr(Nodes.Ident("ranFinally"), Nodes.Literal(true))),
    });

    var statements = new List<Stmt>
    {
      new TryStmt(tryBlock, System.Array.Empty<CatchClause>(), finallyBlock),
    };

    Assert.Equal(RunResult.Awaiting, cursor.Start(statements, environment));

    var result = cursor.Resume(new IntValue(5));

    Assert.Equal(RunResult.Completed, result);
    Assert.Equal(5L, GetInt(environment, "result"));
    Assert.True(environment.TryGet("ranFinally", out var ranFinally));
    Assert.True(Assert.IsType<BoolValue>(ranFinally).Value);
  }

  [Fact]
  public void ResumeFaulted_AwaitInsideTryCatch_IsCaughtByCatchClause()
  {
    var cursor = MakeCursor();
    var environment = new ScriptEnvironment();
    var (pending, _) = PendingThunk(IntType);
    environment.Define("pending", pending);
    environment.Define("caught", BoolValue.Of(false));

    var tryBlock = new BlockStmt(new List<Stmt>
    {
      new VariableDecl(type: null, Nodes.Identifier("x"), new AwaitExpr(Nodes.Token(ALKScriptTokenType.Await, "await"), Nodes.Ident("pending"))),
    });

    var catchBlock = new BlockStmt(new List<Stmt>
    {
      new ExpressionStmt(new AssignmentExpr(Nodes.Ident("caught"), Nodes.Literal(true))),
    });

    var statements = new List<Stmt>
    {
      new TryStmt(tryBlock, new[] { new CatchClause(exceptionType: null, Nodes.Identifier("e"), catchBlock) }, finallyBlock: null),
    };

    Assert.Equal(RunResult.Awaiting, cursor.Start(statements, environment));

    var result = cursor.ResumeFaulted("boom");

    Assert.Equal(RunResult.Completed, result);
    Assert.True(environment.TryGet("caught", out var caught));
    Assert.True(Assert.IsType<BoolValue>(caught).Value);
  }

  [Fact]
  public void Eval_AwaitInBinaryOperandPosition_ThrowsRuntimeException()
  {
    var cursor = MakeCursor();
    var environment = new ScriptEnvironment();
    var (pending, _) = PendingThunk(IntType);
    environment.Define("pending", pending);

    var statements = new List<Stmt>
    {
      new VariableDecl(
        type: null,
        Nodes.Identifier("y"),
        new BinaryExpr(Nodes.Literal(1L), Nodes.Operator(ALKScriptTokenType.Plus, "+"), new AwaitExpr(Nodes.Token(ALKScriptTokenType.Await, "await"), Nodes.Ident("pending")))),
    };

    var exception = Assert.Throws<RuntimeException>(() => cursor.Start(statements, environment));
    Assert.Contains("cannot suspend", exception.Message);
  }

  [Fact]
  public void Execute_ForLoopInitializerAwait_ThrowsRuntimeException()
  {
    var cursor = MakeCursor();
    var environment = new ScriptEnvironment();
    var (pending, _) = PendingThunk(IntType);
    environment.Define("pending", pending);

    var initializer = new VariableDecl(type: null, Nodes.Identifier("i"), new AwaitExpr(Nodes.Token(ALKScriptTokenType.Await, "await"), Nodes.Ident("pending")));
    var forStmt = new ForStmt(initializer, condition: null, increment: null, body: new BlockStmt(new List<Stmt>()));

    var exception = Assert.Throws<RuntimeException>(() => cursor.Start(new List<Stmt> { forStmt }, environment));
    Assert.Contains("for", exception.Message);
  }

  [Fact]
  public void Eval_AwaitArrayOfAlreadyResolvedThunks_ReturnsArrayOfResolvedValues()
  {
    var cursor = MakeCursor();
    var environment = new ScriptEnvironment();
    environment.Define("a", ThunkValue.FromResult(new IntValue(1)));
    environment.Define("b", ThunkValue.FromResult(new IntValue(2)));

    var statements = new List<Stmt>
    {
      new VariableDecl(
        type: null,
        Nodes.Identifier("results"),
        new AwaitExpr(Nodes.Token(ALKScriptTokenType.Await, "await"), new ArrayLiteralExpr(new List<Expr> { Nodes.Ident("a"), Nodes.Ident("b") }))),
    };

    var result = cursor.Start(statements, environment);

    Assert.Equal(RunResult.Completed, result);
    Assert.True(environment.TryGet("results", out var value));
    var array = Assert.IsType<ArrayValue>(value);
    Assert.Equal(1L, Assert.IsType<IntValue>(array.Items[0]).Value);
    Assert.Equal(2L, Assert.IsType<IntValue>(array.Items[1]).Value);
  }

  [Fact]
  public void Eval_AwaitArrayWithUnresolvedElement_ReturnsAwaitingThenResumesWithArrayOfResolvedValues()
  {
    var cursor = MakeCursor();
    var environment = new ScriptEnvironment();
    environment.Define("a", ThunkValue.FromResult(new IntValue(1)));
    var (pending, source) = PendingThunk();
    environment.Define("b", pending);

    var statements = new List<Stmt>
    {
      new VariableDecl(
        type: null,
        Nodes.Identifier("results"),
        new AwaitExpr(Nodes.Token(ALKScriptTokenType.Await, "await"), new ArrayLiteralExpr(new List<Expr> { Nodes.Ident("a"), Nodes.Ident("b") }))),
    };

    var startResult = cursor.Start(statements, environment);

    Assert.Equal(RunResult.Awaiting, startResult);
    Assert.NotNull(cursor.PendingAwait!.CompositeElements);

    source.SetResult(new IntValue(2));
    pending.Poll();
    var result = cursor.Resume(NullValue.Instance);

    Assert.Equal(RunResult.Completed, result);
    Assert.True(environment.TryGet("results", out var value));
    var array = Assert.IsType<ArrayValue>(value);
    Assert.Equal(1L, Assert.IsType<IntValue>(array.Items[0]).Value);
    Assert.Equal(2L, Assert.IsType<IntValue>(array.Items[1]).Value);
  }
}
