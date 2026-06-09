using System.Collections.Generic;
using System.Threading.Tasks;
using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Common.Evaluation;
using ALKScript.Interpreter.Common.Evaluation.Values;

namespace ALKScript.Interpreter.Evaluator
{
  /// <summary>
  /// Executes statements: dispatches on <see cref="Stmt"/> shape and drives
  /// control flow (blocks, conditionals, loops, "return"/"throw"/"try").
  /// Expression evaluation and calls are delegated through
  /// <see cref="IEvaluationContext"/>.
  ///
  /// Every method here is <c>async</c>/<see cref="Task"/>-returning — not
  /// because anything actually suspends yet (Phase 2 introduces no new
  /// behavior; everything still resolves synchronously), but because this is
  /// the plumbing <c>await</c> needs: turning each method into a
  /// compiler-generated continuation lets a future suspension (e.g. on a
  /// pending host operation) unwind through this exact call chain and later
  /// resume it without losing any in-flight control-flow state — see
  /// <see cref="IEvaluationContext"/> for the fuller rationale.
  /// </summary>
  internal class StatementExecutor : IStatementExecutor
  {
    private readonly IEvaluationContext _context;
    private readonly IFunctionValueFactory _functionValueFactory;

    public StatementExecutor(IEvaluationContext context, IFunctionValueFactory functionValueFactory)
    {
      _context = context;
      _functionValueFactory = functionValueFactory;
    }

    public async Task Execute(Stmt statement, ScriptEnvironment environment)
    {
      if (_context.Signal != null)
      {
        return;
      }

      switch (statement)
      {
        case StatementDecl statementDecl:
          await Execute(statementDecl.Statement, environment);
          break;

        case VariableDecl variableDecl:
          await ExecuteVariableDecl(variableDecl, environment);
          break;

        case FunctionDecl functionDecl:
          environment.Define(functionDecl.Name.Lexeme, _functionValueFactory.Create(functionDecl, environment));
          break;

        case ClassDecl classDecl:
          ExecuteClassDecl(classDecl, environment);
          break;

        case ExportDecl exportDecl:
          await Execute(exportDecl.Declaration, environment);
          break;

        case ExpressionStmt expressionStmt:
          await _context.Eval(expressionStmt.Expression, environment);
          break;

        case BlockStmt blockStmt:
          await ExecuteBlock(blockStmt.Statements, new ScriptEnvironment(environment));
          break;

        case IfStmt ifStmt:
          await ExecuteIf(ifStmt, environment);
          break;

        case WhileStmt whileStmt:
          await ExecuteWhile(whileStmt, environment);
          break;

        case ForStmt forStmt:
          await ExecuteFor(forStmt, environment);
          break;

        case ForeachStmt foreachStmt:
          await ExecuteForeach(foreachStmt, environment);
          break;

        case DoWhileStmt doWhileStmt:
          await ExecuteDoWhile(doWhileStmt, environment);
          break;

        case BreakStmt breakStmt:
          _context.Signal = Signal.Break();
          break;

        case ContinueStmt continueStmt:
          _context.Signal = Signal.Continue();
          break;

        case ReturnStmt returnStmt:
          await ExecuteReturn(returnStmt, environment);
          break;

        case ThrowStmt throwStmt:
          await ExecuteThrow(throwStmt, environment);
          break;

        case TryStmt tryStmt:
          await ExecuteTry(tryStmt, environment);
          break;

        default:
          throw new RuntimeException(
            AstTokenLocator.Of(statement),
            $"Execution of '{statement.GetType().Name}' is not yet supported.");
      }
    }

    public async Task ExecuteBlock(IReadOnlyList<Stmt> statements, ScriptEnvironment environment)
    {
      foreach (var statement in statements)
      {
        await Execute(statement, environment);

        if (_context.Signal != null)
        {
          return;
        }
      }
    }

    private async Task ExecuteVariableDecl(VariableDecl declaration, ScriptEnvironment environment)
    {
      ALKScriptValue value = NullValue.Instance;

      if (declaration.Initializer != null)
      {
        value = await _context.Eval(declaration.Initializer, environment);

        if (_context.Signal != null)
        {
          return;
        }
      }

      environment.Define(declaration.Name.Lexeme, value);
    }

    private void ExecuteClassDecl(ClassDecl declaration, ScriptEnvironment environment)
    {
      ClassValue? superclass = null;

      if (declaration.SuperclassName != null)
      {
        var superclassValue = Names.LookUp(declaration.SuperclassName, environment);
        superclass = superclassValue as ClassValue
          ?? throw new RuntimeException(declaration.SuperclassName, $"'{declaration.SuperclassName.Lexeme}' is not a class.");
      }

      environment.Define(declaration.Name.Lexeme, new ClassValue(declaration, superclass, environment));
    }

    private async Task ExecuteIf(IfStmt statement, ScriptEnvironment environment)
    {
      var condition = await _context.Eval(statement.Condition, environment);

      if (_context.Signal != null)
      {
        return;
      }

      if (condition.IsTruthy)
      {
        await Execute(statement.ThenBranch, environment);
      }
      else if (statement.ElseBranch != null)
      {
        await Execute(statement.ElseBranch, environment);
      }
    }

    private async Task ExecuteWhile(WhileStmt statement, ScriptEnvironment environment)
    {
      while (true)
      {
        var condition = await _context.Eval(statement.Condition, environment);

        if (_context.Signal != null)
        {
          return;
        }

        if (!condition.IsTruthy)
        {
          return;
        }

        await Execute(statement.Body, environment);

        if (_context.Signal != null)
        {
          if (_context.Signal.Value.Kind == SignalKind.Break)
          {
            _context.Signal = null;
            return;
          }

          if (_context.Signal.Value.Kind == SignalKind.Continue)
          {
            _context.Signal = null;
            continue;
          }

          return;
        }
      }
    }

    private async Task ExecuteFor(ForStmt statement, ScriptEnvironment environment)
    {
      var loopEnvironment = new ScriptEnvironment(environment);

      if (statement.Initializer != null)
      {
        await Execute(statement.Initializer, loopEnvironment);

        if (_context.Signal != null)
        {
          return;
        }
      }

      while (true)
      {
        if (statement.Condition != null)
        {
          var condition = await _context.Eval(statement.Condition, loopEnvironment);

          if (_context.Signal != null)
          {
            return;
          }

          if (!condition.IsTruthy)
          {
            return;
          }
        }

        await Execute(statement.Body, loopEnvironment);

        if (_context.Signal != null)
        {
          if (_context.Signal.Value.Kind == SignalKind.Break)
          {
            _context.Signal = null;
            return;
          }

          if (_context.Signal.Value.Kind != SignalKind.Continue)
          {
            return;
          }

          _context.Signal = null; // continue — fall through to increment
        }

        if (statement.Increment != null)
        {
          await _context.Eval(statement.Increment, loopEnvironment);

          if (_context.Signal != null)
          {
            return;
          }
        }
      }
    }

    private async Task ExecuteForeach(ForeachStmt statement, ScriptEnvironment environment)
    {
      var collectionValue = await _context.Eval(statement.Collection, environment);

      if (_context.Signal != null)
      {
        return;
      }

      var array = collectionValue as ArrayValue
        ?? throw new RuntimeException(statement.Keyword, $"'foreach' requires an array but got '{collectionValue.TypeName}'.");

      foreach (var item in array.Items)
      {
        var loopEnvironment = new ScriptEnvironment(environment);
        loopEnvironment.Define(statement.Variable.Lexeme, item);

        await Execute(statement.Body, loopEnvironment);

        if (_context.Signal != null)
        {
          if (_context.Signal.Value.Kind == SignalKind.Break)
          {
            _context.Signal = null;
            return;
          }

          if (_context.Signal.Value.Kind == SignalKind.Continue)
          {
            _context.Signal = null;
            continue;
          }

          return;
        }
      }
    }

    private async Task ExecuteDoWhile(DoWhileStmt statement, ScriptEnvironment environment)
    {
      while (true)
      {
        await Execute(statement.Body, environment);

        if (_context.Signal != null)
        {
          if (_context.Signal.Value.Kind == SignalKind.Break)
          {
            _context.Signal = null;
            return;
          }

          if (_context.Signal.Value.Kind == SignalKind.Continue)
          {
            _context.Signal = null;
            // fall through to condition check
          }
          else
          {
            return;
          }
        }

        var condition = await _context.Eval(statement.Condition, environment);

        if (_context.Signal != null)
        {
          return;
        }

        if (!condition.IsTruthy)
        {
          return;
        }
      }
    }

    private async Task ExecuteReturn(ReturnStmt statement, ScriptEnvironment environment)
    {
      var value = NullValue.Instance as ALKScriptValue;

      if (statement.Value != null)
      {
        value = await _context.Eval(statement.Value, environment);

        if (_context.Signal != null)
        {
          return;
        }
      }

      _context.Signal = Signal.Return(value);
    }

    private async Task ExecuteThrow(ThrowStmt statement, ScriptEnvironment environment)
    {
      var value = await _context.Eval(statement.Value, environment);

      if (_context.Signal != null)
      {
        return;
      }

      _context.Signal = Signal.Thrown(value);
    }

    private async Task ExecuteTry(TryStmt statement, ScriptEnvironment environment)
    {
      await ExecuteBlock(statement.TryBlock.Statements, new ScriptEnvironment(environment));

      if (_context.Signal is { Kind: SignalKind.Thrown } thrown)
      {
        _context.Signal = null;

        if (!await TryHandle(statement.CatchClauses, thrown.Value, environment) && _context.Signal == null)
        {
          _context.Signal = thrown;
        }
      }

      if (statement.FinallyBlock != null)
      {
        var pending = _context.Signal;
        _context.Signal = null;

        await ExecuteBlock(statement.FinallyBlock.Statements, new ScriptEnvironment(environment));

        // A "return"/"throw" raised by the "finally" block overrides whatever
        // was pending beforehand — matching ordinary try/finally semantics.
        if (_context.Signal == null)
        {
          _context.Signal = pending;
        }
      }
    }

    private async Task<bool> TryHandle(IReadOnlyList<CatchClause> clauses, ALKScriptValue thrown, ScriptEnvironment environment)
    {
      foreach (var clause in clauses)
      {
        var catchEnvironment = new ScriptEnvironment(environment);

        if (clause.ExceptionName != null)
        {
          catchEnvironment.Define(clause.ExceptionName.Lexeme, thrown);
        }

        await ExecuteBlock(clause.Body.Statements, catchEnvironment);
        return true;
      }

      return false;
    }
  }
}
