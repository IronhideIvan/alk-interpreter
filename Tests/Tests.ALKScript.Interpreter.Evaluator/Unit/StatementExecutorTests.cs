using System.Collections.Generic;
using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Common.Evaluation;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Common.Token;
using ALKScript.Interpreter.Evaluator;

namespace Tests.ALKScript.Interpreter.Evaluator.Unit;

public class StatementExecutorTests
{
  private static StatementExecutor MakeExecutor(FakeEvaluationContext context, IFunctionValueFactory? functionValueFactory = null) =>
    new StatementExecutor(context, functionValueFactory ?? new FunctionValueFactory());

  [Fact]
  public void Execute_ExpressionStatement_EvaluatesItsExpressionThroughTheContext()
  {
    var context = new FakeEvaluationContext();
    var expression = Nodes.Literal(1L);
    Expr? evaluated = null;
    context.EvalImpl = (expr, _) => { evaluated = expr; return NullValue.Instance; };

    MakeExecutor(context).Execute(new ExpressionStmt(expression), new ScriptEnvironment());

    Assert.Same(expression, evaluated);
  }

  [Fact]
  public void Execute_VariableDeclWithInitializer_DefinesEvaluatedValue()
  {
    var context = new FakeEvaluationContext();
    context.EvalImpl = (_, _) => new IntValue(7);
    var environment = new ScriptEnvironment();

    MakeExecutor(context).Execute(new VariableDecl(null, Nodes.Identifier("x"), Nodes.Literal(7L)), environment);

    Assert.True(environment.TryGet("x", out var value));
    Assert.Equal(7L, Assert.IsType<IntValue>(value).Value);
  }

  [Fact]
  public void Execute_VariableDeclWithoutInitializer_DefinesNull()
  {
    var context = new FakeEvaluationContext();
    var environment = new ScriptEnvironment();

    MakeExecutor(context).Execute(new VariableDecl(null, Nodes.Identifier("x"), null), environment);

    Assert.True(environment.TryGet("x", out var value));
    Assert.Same(NullValue.Instance, value);
  }

  [Fact]
  public void Execute_VariableDecl_DoesNotDefineWhenInitializerRaisesASignal()
  {
    var context = new FakeEvaluationContext();
    context.EvalImpl = (_, _) => { context.Signal = Signal.Thrown(new IntValue(1)); return NullValue.Instance; };
    var environment = new ScriptEnvironment();

    MakeExecutor(context).Execute(new VariableDecl(null, Nodes.Identifier("x"), Nodes.Literal(1L)), environment);

    Assert.False(environment.TryGet("x", out _));
  }

  [Theory]
  [InlineData(true, "then")]
  [InlineData(false, "else")]
  public void Execute_IfStatement_RunsTheBranchMatchingConditionTruthiness(bool conditionIsTruthy, string expectedBranch)
  {
    // "If" dispatches its chosen branch via Execute() recursing into itself
    // (not through the context), so observe which one ran by the variable it
    // defines — a real side effect of self-recursive statement execution.
    var context = new FakeEvaluationContext { EvalImpl = (_, _) => BoolValue.Of(conditionIsTruthy) };
    var ifStmt = new IfStmt(
      Nodes.Literal(conditionIsTruthy),
      thenBranch: new VariableDecl(null, Nodes.Identifier("then"), null),
      elseBranch: new VariableDecl(null, Nodes.Identifier("else"), null));
    var environment = new ScriptEnvironment();

    MakeExecutor(context).Execute(ifStmt, environment);

    Assert.True(environment.TryGet(expectedBranch, out _));
    Assert.False(environment.TryGet(expectedBranch == "then" ? "else" : "then", out _));
  }

  [Fact]
  public void Execute_ReturnStatement_SetsReturnSignalToEvaluatedValue()
  {
    var context = new FakeEvaluationContext();
    context.EvalImpl = (_, _) => new IntValue(5);

    MakeExecutor(context).Execute(new ReturnStmt(Nodes.Token(ALKScriptTokenType.Identifier, "return"), Nodes.Literal(5L)), new ScriptEnvironment());

    Assert.NotNull(context.Signal);
    Assert.Equal(SignalKind.Return, context.Signal!.Value.Kind);
    Assert.Equal(5L, Assert.IsType<IntValue>(context.Signal.Value.Value).Value);
  }

  [Fact]
  public void Execute_ReturnStatementWithoutValue_SetsReturnSignalToNull()
  {
    var context = new FakeEvaluationContext();

    MakeExecutor(context).Execute(new ReturnStmt(Nodes.Token(ALKScriptTokenType.Identifier, "return"), null), new ScriptEnvironment());

    Assert.Equal(SignalKind.Return, context.Signal!.Value.Kind);
    Assert.Same(NullValue.Instance, context.Signal.Value.Value);
  }

  [Fact]
  public void Execute_ThrowStatement_SetsThrownSignalToEvaluatedValue()
  {
    var context = new FakeEvaluationContext();
    context.EvalImpl = (_, _) => new StringValue("boom");

    MakeExecutor(context).Execute(new ThrowStmt(Nodes.Token(ALKScriptTokenType.Identifier, "throw"), Nodes.Literal("boom")), new ScriptEnvironment());

    Assert.Equal(SignalKind.Thrown, context.Signal!.Value.Kind);
    Assert.Equal("boom", Assert.IsType<StringValue>(context.Signal.Value.Value).Value);
  }

  [Fact]
  public void Execute_TryWithCancelledSignalDuringTryBlock_BypassesCatchClauses()
  {
    // "Cancelled" is an uncatchable unwind — unlike "Thrown", it must pass
    // straight through any "catch" clauses without invoking them, leaving the
    // pending "Cancelled" signal in place so the unwind continues outward.
    // Each statement's expression is a distinctly-tagged literal; the fake
    // EvalImpl records which tags actually got evaluated and raises the
    // "Cancelled" signal when it sees "trigger-cancel" — modelling an
    // external stop request arriving mid-try-block.
    var context = new FakeEvaluationContext();
    var evaluatedTags = new List<string>();
    context.EvalImpl = (expr, _) =>
    {
      var tag = (string)((LiteralExpr)expr).Value!;
      evaluatedTags.Add(tag);

      if (tag == "trigger-cancel")
      {
        context.Signal = Signal.Cancelled();
      }

      return NullValue.Instance;
    };

    var tryStmt = new TryStmt(
      tryBlock: new BlockStmt(new Stmt[] { new ExpressionStmt(Nodes.Literal("trigger-cancel")) }),
      catchClauses: new[]
      {
        new CatchClause(null, Nodes.Identifier("e"), new BlockStmt(new Stmt[] { new ExpressionStmt(Nodes.Literal("caught")) })),
      },
      finallyBlock: null);

    MakeExecutor(context).Execute(tryStmt, new ScriptEnvironment());

    Assert.Equal(new[] { "trigger-cancel" }, evaluatedTags);
    Assert.NotNull(context.Signal);
    Assert.Equal(SignalKind.Cancelled, context.Signal!.Value.Kind);
  }

  [Fact]
  public void Execute_TryWithCancelledSignalDuringTryBlock_StillRunsFinallyBlock()
  {
    // "Finally" blocks must still run on a "Cancelled" unwind so resources get
    // cleaned up — and the pending "Cancelled" signal is restored once
    // "finally" completes without itself raising an overriding signal.
    var context = new FakeEvaluationContext();
    var evaluatedTags = new List<string>();
    context.EvalImpl = (expr, _) =>
    {
      var tag = (string)((LiteralExpr)expr).Value!;
      evaluatedTags.Add(tag);

      if (tag == "trigger-cancel")
      {
        context.Signal = Signal.Cancelled();
      }

      return NullValue.Instance;
    };

    var tryStmt = new TryStmt(
      tryBlock: new BlockStmt(new Stmt[] { new ExpressionStmt(Nodes.Literal("trigger-cancel")) }),
      catchClauses: System.Array.Empty<CatchClause>(),
      finallyBlock: new BlockStmt(new Stmt[] { new ExpressionStmt(Nodes.Literal("finally")) }));

    MakeExecutor(context).Execute(tryStmt, new ScriptEnvironment());

    Assert.Equal(new[] { "trigger-cancel", "finally" }, evaluatedTags);
    Assert.NotNull(context.Signal);
    Assert.Equal(SignalKind.Cancelled, context.Signal!.Value.Kind);
  }

  [Fact]
  public void Execute_AlreadyPendingSignal_SkipsExecutionEntirely()
  {
    var context = new FakeEvaluationContext { Signal = Signal.Return(NullValue.Instance) };
    var evaluated = false;
    context.EvalImpl = (_, _) => { evaluated = true; return NullValue.Instance; };

    MakeExecutor(context).Execute(new ExpressionStmt(Nodes.Literal(1L)), new ScriptEnvironment());

    Assert.False(evaluated);
  }

  [Fact]
  public void Execute_UnsupportedStatementKind_ThrowsRuntimeException()
  {
    var context = new FakeEvaluationContext();

    var exception = Assert.Throws<RuntimeException>(() => MakeExecutor(context).Execute(new UnsupportedStmt(), new ScriptEnvironment()));

    Assert.Contains("is not yet supported", exception.Message);
  }

  [Fact]
  public void ExecuteBlock_StopsAtFirstStatementThatRaisesASignal()
  {
    var context = new FakeEvaluationContext();
    context.EvalImpl = (_, _) => { context.Signal = Signal.Thrown(NullValue.Instance); return NullValue.Instance; };

    var statements = new List<Stmt>
    {
      new ThrowStmt(Nodes.Token(ALKScriptTokenType.Identifier, "throw"), Nodes.Literal(1L)),
      new VariableDecl(null, Nodes.Identifier("unreached"), null),
    };
    var environment = new ScriptEnvironment();

    MakeExecutor(context).ExecuteBlock(statements, environment);

    Assert.False(environment.TryGet("unreached", out _));
  }

  [Fact]
  public void Execute_FunctionDecl_DefinesItsValueViaTheFunctionValueFactory()
  {
    var declaration = new FunctionDecl(false, false, System.Array.Empty<string>(), Nodes.VoidType, Nodes.Identifier("greet"), System.Array.Empty<Parameter>(), new BlockStmt(System.Array.Empty<Stmt>()));
    var context = new FakeEvaluationContext();
    var environment = new ScriptEnvironment();

    new StatementExecutor(context, new FunctionValueFactory()).Execute(declaration, environment);

    Assert.True(environment.TryGet("greet", out var value));
    var function = Assert.IsType<FunctionValue>(value);
    Assert.Same(declaration, function.Declaration);
  }

  [Fact]
  public void Execute_ClassDeclWithSuperclassNameThatIsNotAClass_ThrowsRuntimeException()
  {
    var context = new FakeEvaluationContext();
    var environment = new ScriptEnvironment();
    environment.Define("Missing", new IntValue(1));
    var declaration = new ClassDecl(
      false,
      Nodes.Identifier("Derived"),
      System.Array.Empty<string>(),
      superclassName: Nodes.Identifier("Missing"),
      superclassTypeArguments: System.Array.Empty<TypeNode>(),
      members: System.Array.Empty<MemberDecl>());

    var exception = Assert.Throws<RuntimeException>(() => MakeExecutor(context).Execute(declaration, environment));

    Assert.Contains("'Missing' is not a class", exception.Message);
  }

  /// <summary>An AST statement shape the evaluator deliberately does not support, for testing the fallback dispatch branch.</summary>
  private sealed class UnsupportedStmt : Stmt
  {
  }
}
