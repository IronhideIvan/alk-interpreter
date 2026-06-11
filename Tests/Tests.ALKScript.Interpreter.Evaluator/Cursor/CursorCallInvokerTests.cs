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
/// Step 4 coverage for the cursor-rewrite plan (docs:
/// validated-nibbling-narwhal): <see cref="CallExpr"/>, <see cref="NewExpr"/>,
/// and <see cref="CursorCallInvoker"/> — function calls, constructors with
/// field initializers, and array <c>map</c>/<c>filter</c> (which call back
/// into the cursor). All evaluated synchronously via
/// <see cref="EvaluationCursor.Eval"/> with no suspension possible yet.
/// </summary>
public class CursorCallInvokerTests
{
  private static EvaluationCursor MakeCursor() => new EvaluationCursor(new FunctionValueFactory());

  private static ALKScriptValue EvalCompleted(EvaluationCursor cursor, Expr expression, ScriptEnvironment environment)
  {
    var step = cursor.Eval(expression, environment);
    Assert.False(step.IsAwaiting);
    return step.Value!;
  }

  private static TypeNode IntType => new TypeNode("int", System.Array.Empty<TypeNode>(), 0, false);

  [Fact]
  public void Eval_Call_InvokesAFunctionAndReturnsItsReturnValue()
  {
    var cursor = MakeCursor();
    var environment = new ScriptEnvironment();

    // function int add(int a, int b) { return a + b; }
    var declaration = new FunctionDecl(
      isNative: false,
      typeParameters: System.Array.Empty<string>(),
      returnType: IntType,
      name: Nodes.Identifier("add"),
      parameters: new[] { new Parameter(IntType, "a"), new Parameter(IntType, "b") },
      body: new BlockStmt(new List<Stmt>
      {
        new ReturnStmt(Nodes.Token(ALKScriptTokenType.Return, "return"),
          new BinaryExpr(Nodes.Ident("a"), Nodes.Operator(ALKScriptTokenType.Plus, "+"), Nodes.Ident("b"))),
      }));

    var function = new FunctionValue(declaration, environment);
    environment.Define("add", function);

    var call = new CallExpr(Nodes.Ident("add"), Nodes.Token(ALKScriptTokenType.RightParen, ")"), new Expr[] { Nodes.Literal(2L), Nodes.Literal(3L) });

    var value = EvalCompleted(cursor, call, environment);

    Assert.Equal(5L, Assert.IsType<IntValue>(value).Value);
    Assert.Null(cursor.Signal);
  }

  [Fact]
  public void Eval_New_InitializesFieldsAndRunsConstructor()
  {
    var cursor = MakeCursor();
    var environment = new ScriptEnvironment();

    // class Point {
    //   int x = 0;
    //   int y;
    //   new(int x, int y) { this.x = x; this.y = y; }
    // }
    var members = new List<MemberDecl>
    {
      new FieldDecl(AccessModifier.Public, IntType, Nodes.Identifier("x"), Nodes.Literal(0L)),
      new FieldDecl(AccessModifier.Public, IntType, Nodes.Identifier("y"), initializer: null),
      new ConstructorDecl(AccessModifier.Public, new[] { new Parameter(IntType, "x"), new Parameter(IntType, "y") },
        new BlockStmt(new List<Stmt>
        {
          new ExpressionStmt(new AssignmentExpr(new GetExpr(Nodes.Ident("this"), Nodes.Identifier("x")), Nodes.Ident("x"))),
          new ExpressionStmt(new AssignmentExpr(new GetExpr(Nodes.Ident("this"), Nodes.Identifier("y")), Nodes.Ident("y"))),
        })),
    };

    var classDecl = new ClassDecl(false, Nodes.Identifier("Point"), System.Array.Empty<string>(), null, System.Array.Empty<TypeNode>(), members);
    var classValue = new ClassValue(classDecl, null, environment);
    environment.Define("Point", classValue);

    var newExpr = new NewExpr(
      Nodes.Token(ALKScriptTokenType.New, "new"),
      Nodes.Identifier("Point"),
      System.Array.Empty<TypeNode>(),
      new Expr[] { Nodes.Literal(10L), Nodes.Literal(20L) });

    var value = EvalCompleted(cursor, newExpr, environment);

    var instance = Assert.IsType<InstanceValue>(value);
    Assert.Equal(10L, Assert.IsType<IntValue>(instance.Fields["x"]).Value);
    Assert.Equal(20L, Assert.IsType<IntValue>(instance.Fields["y"]).Value);
  }

  [Fact]
  public void Eval_ArrayMap_AppliesCallbackToEachElement()
  {
    var cursor = MakeCursor();
    var environment = new ScriptEnvironment();

    // function int double(int x) { return x * 2; }
    var doubleDecl = new FunctionDecl(
      isNative: false,
      typeParameters: System.Array.Empty<string>(),
      returnType: IntType,
      name: Nodes.Identifier("double"),
      parameters: new[] { new Parameter(IntType, "x") },
      body: new BlockStmt(new List<Stmt>
      {
        new ReturnStmt(Nodes.Token(ALKScriptTokenType.Return, "return"),
          new BinaryExpr(Nodes.Ident("x"), Nodes.Operator(ALKScriptTokenType.Star, "*"), Nodes.Literal(2L))),
      }));

    environment.Define("arr", new ArrayValue(new List<ALKScriptValue> { new IntValue(1), new IntValue(2), new IntValue(3) }));
    environment.Define("double", new FunctionValue(doubleDecl, environment));

    // arr.map(double)
    var mapCall = new CallExpr(
      new GetExpr(Nodes.Ident("arr"), Nodes.Identifier("map")),
      Nodes.Token(ALKScriptTokenType.RightParen, ")"),
      new Expr[] { Nodes.Ident("double") });

    var value = Assert.IsType<ArrayValue>(EvalCompleted(cursor, mapCall, environment));

    Assert.Equal(new long[] { 2, 4, 6 }, value.Items.ConvertAll(item => ((IntValue)item).Value));
  }
}
