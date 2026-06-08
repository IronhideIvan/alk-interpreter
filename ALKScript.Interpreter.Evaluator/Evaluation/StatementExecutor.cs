using System.Collections.Generic;
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

    public void Execute(Stmt statement, ScriptEnvironment environment)
    {
      if (_context.Signal != null)
      {
        return;
      }

      switch (statement)
      {
        case StatementDecl statementDecl:
          Execute(statementDecl.Statement, environment);
          break;

        case VariableDecl variableDecl:
          ExecuteVariableDecl(variableDecl, environment);
          break;

        case FunctionDecl functionDecl:
          environment.Define(functionDecl.Name.Lexeme, _functionValueFactory.Create(functionDecl, environment));
          break;

        case ClassDecl classDecl:
          ExecuteClassDecl(classDecl, environment);
          break;

        case ExportDecl exportDecl:
          Execute(exportDecl.Declaration, environment);
          break;

        case ExpressionStmt expressionStmt:
          _context.Eval(expressionStmt.Expression, environment);
          break;

        case BlockStmt blockStmt:
          ExecuteBlock(blockStmt.Statements, new ScriptEnvironment(environment));
          break;

        case IfStmt ifStmt:
          ExecuteIf(ifStmt, environment);
          break;

        case WhileStmt whileStmt:
          ExecuteWhile(whileStmt, environment);
          break;

        case ForStmt forStmt:
          ExecuteFor(forStmt, environment);
          break;

        case ReturnStmt returnStmt:
          ExecuteReturn(returnStmt, environment);
          break;

        case ThrowStmt throwStmt:
          ExecuteThrow(throwStmt, environment);
          break;

        case TryStmt tryStmt:
          ExecuteTry(tryStmt, environment);
          break;

        default:
          throw new RuntimeException(
            AstTokenLocator.Of(statement),
            $"Execution of '{statement.GetType().Name}' is not yet supported.");
      }
    }

    public void ExecuteBlock(IReadOnlyList<Stmt> statements, ScriptEnvironment environment)
    {
      foreach (var statement in statements)
      {
        Execute(statement, environment);

        if (_context.Signal != null)
        {
          return;
        }
      }
    }

    private void ExecuteVariableDecl(VariableDecl declaration, ScriptEnvironment environment)
    {
      ALKScriptValue value = NullValue.Instance;

      if (declaration.Initializer != null)
      {
        value = _context.Eval(declaration.Initializer, environment);

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

    private void ExecuteIf(IfStmt statement, ScriptEnvironment environment)
    {
      var condition = _context.Eval(statement.Condition, environment);

      if (_context.Signal != null)
      {
        return;
      }

      if (condition.IsTruthy)
      {
        Execute(statement.ThenBranch, environment);
      }
      else if (statement.ElseBranch != null)
      {
        Execute(statement.ElseBranch, environment);
      }
    }

    private void ExecuteWhile(WhileStmt statement, ScriptEnvironment environment)
    {
      while (true)
      {
        var condition = _context.Eval(statement.Condition, environment);

        if (_context.Signal != null)
        {
          return;
        }

        if (!condition.IsTruthy)
        {
          return;
        }

        Execute(statement.Body, environment);

        if (_context.Signal != null)
        {
          return;
        }
      }
    }

    private void ExecuteFor(ForStmt statement, ScriptEnvironment environment)
    {
      var loopEnvironment = new ScriptEnvironment(environment);

      if (statement.Initializer != null)
      {
        Execute(statement.Initializer, loopEnvironment);

        if (_context.Signal != null)
        {
          return;
        }
      }

      while (true)
      {
        if (statement.Condition != null)
        {
          var condition = _context.Eval(statement.Condition, loopEnvironment);

          if (_context.Signal != null)
          {
            return;
          }

          if (!condition.IsTruthy)
          {
            return;
          }
        }

        Execute(statement.Body, loopEnvironment);

        if (_context.Signal != null)
        {
          return;
        }

        if (statement.Increment != null)
        {
          _context.Eval(statement.Increment, loopEnvironment);

          if (_context.Signal != null)
          {
            return;
          }
        }
      }
    }

    private void ExecuteReturn(ReturnStmt statement, ScriptEnvironment environment)
    {
      var value = NullValue.Instance as ALKScriptValue;

      if (statement.Value != null)
      {
        value = _context.Eval(statement.Value, environment);

        if (_context.Signal != null)
        {
          return;
        }
      }

      _context.Signal = Signal.Return(value);
    }

    private void ExecuteThrow(ThrowStmt statement, ScriptEnvironment environment)
    {
      var value = _context.Eval(statement.Value, environment);

      if (_context.Signal != null)
      {
        return;
      }

      _context.Signal = Signal.Thrown(value);
    }

    private void ExecuteTry(TryStmt statement, ScriptEnvironment environment)
    {
      ExecuteBlock(statement.TryBlock.Statements, new ScriptEnvironment(environment));

      if (_context.Signal is { Kind: SignalKind.Thrown } thrown)
      {
        _context.Signal = null;

        if (!TryHandle(statement.CatchClauses, thrown.Value, environment) && _context.Signal == null)
        {
          _context.Signal = thrown;
        }
      }

      if (statement.FinallyBlock != null)
      {
        var pending = _context.Signal;
        _context.Signal = null;

        ExecuteBlock(statement.FinallyBlock.Statements, new ScriptEnvironment(environment));

        // A "return"/"throw" raised by the "finally" block overrides whatever
        // was pending beforehand — matching ordinary try/finally semantics.
        if (_context.Signal == null)
        {
          _context.Signal = pending;
        }
      }
    }

    private bool TryHandle(IReadOnlyList<CatchClause> clauses, ALKScriptValue thrown, ScriptEnvironment environment)
    {
      foreach (var clause in clauses)
      {
        var catchEnvironment = new ScriptEnvironment(environment);

        if (clause.ExceptionName != null)
        {
          catchEnvironment.Define(clause.ExceptionName.Lexeme, thrown);
        }

        ExecuteBlock(clause.Body.Statements, catchEnvironment);
        return true;
      }

      return false;
    }
  }
}
