using System.Collections.Generic;
using ALKScript.Interpreter.Common.Ast;

namespace ALKScript.Interpreter.Evaluator.Cursor
{
  /// <summary>
  /// Recursively searches a declaration's body for a <see cref="LambdaExpr"/>
  /// whose <see cref="LambdaExpr.Arrow"/> token is at a given source
  /// position — the inverse of how <c>CursorExpressionEvaluator.EvalLambda</c>
  /// synthesizes a <see cref="FunctionDecl"/> with name token
  /// <c>"&lt;lambda&gt;"</c> at <c>lambda.Arrow.Line/Column</c>. Used by
  /// <see cref="AstResolver"/>'s lambda addressing (the "Phase B" structural
  /// Capture/Restore design, docs/ASYNC_AWAIT_DESIGN.md Addendum 3).
  /// </summary>
  internal static class AstLambdaFinder
  {
    public static LambdaExpr? FindInStatements(IReadOnlyList<Stmt> statements, int line, int column)
    {
      foreach (var stmt in statements)
      {
        var found = FindInStmt(stmt, line, column);
        if (found != null)
        {
          return found;
        }
      }

      return null;
    }

    public static LambdaExpr? FindInStmt(Stmt? stmt, int line, int column)
    {
      switch (stmt)
      {
        case null:
          return null;

        case BlockStmt block:
          return FindInStatements(block.Statements, line, column);

        case StatementDecl statementDecl:
          return FindInStmt(statementDecl.Statement, line, column);

        case ExpressionStmt expressionStmt:
          return FindInExpr(expressionStmt.Expression, line, column);

        case VariableDecl variableDecl:
          return FindInExpr(variableDecl.Initializer, line, column);

        case IfStmt ifStmt:
          return FindInExpr(ifStmt.Condition, line, column)
            ?? FindInStmt(ifStmt.ThenBranch, line, column)
            ?? FindInStmt(ifStmt.ElseBranch, line, column);

        case WhileStmt whileStmt:
          return FindInExpr(whileStmt.Condition, line, column)
            ?? FindInStmt(whileStmt.Body, line, column);

        case DoWhileStmt doWhileStmt:
          return FindInStmt(doWhileStmt.Body, line, column)
            ?? FindInExpr(doWhileStmt.Condition, line, column);

        case ForStmt forStmt:
          return FindInStmt(forStmt.Initializer, line, column)
            ?? FindInExpr(forStmt.Condition, line, column)
            ?? FindInExpr(forStmt.Increment, line, column)
            ?? FindInStmt(forStmt.Body, line, column);

        case ForeachStmt foreachStmt:
          return FindInExpr(foreachStmt.Collection, line, column)
            ?? FindInStmt(foreachStmt.Body, line, column);

        case ReturnStmt returnStmt:
          return FindInExpr(returnStmt.Value, line, column);

        case ThrowStmt throwStmt:
          return FindInExpr(throwStmt.Value, line, column);

        case TryStmt tryStmt:
          {
            var found = FindInStmt(tryStmt.TryBlock, line, column);
            if (found != null)
            {
              return found;
            }

            foreach (var catchClause in tryStmt.CatchClauses)
            {
              found = FindInStmt(catchClause.Body, line, column);
              if (found != null)
              {
                return found;
              }
            }

            return FindInStmt(tryStmt.FinallyBlock, line, column);
          }

        case SwitchStmt switchStmt:
          {
            var found = FindInExpr(switchStmt.Discriminant, line, column);
            if (found != null)
            {
              return found;
            }

            foreach (var switchCase in switchStmt.Cases)
            {
              found = FindInExpr(switchCase.Test, line, column)
                ?? FindInStatements(switchCase.Body, line, column);
              if (found != null)
              {
                return found;
              }
            }

            return null;
          }

        default:
          return null;
      }
    }

    public static LambdaExpr? FindInExpr(Expr? expr, int line, int column)
    {
      switch (expr)
      {
        case null:
          return null;

        case LambdaExpr lambda:
          if (lambda.Arrow.Line == line && lambda.Arrow.Column == column)
          {
            return lambda;
          }

          return FindInStmt(lambda.Body, line, column);

        case GroupingExpr groupingExpr:
          return FindInExpr(groupingExpr.Expression, line, column);

        case ArrayLiteralExpr arrayLiteralExpr:
          return FindInExprList(arrayLiteralExpr.Elements, line, column);

        case AssignmentExpr assignmentExpr:
          return FindInExpr(assignmentExpr.Target, line, column)
            ?? FindInExpr(assignmentExpr.Value, line, column);

        case BinaryExpr binaryExpr:
          return FindInExpr(binaryExpr.Left, line, column)
            ?? FindInExpr(binaryExpr.Right, line, column);

        case UnaryExpr unaryExpr:
          return FindInExpr(unaryExpr.Operand, line, column);

        case AwaitExpr awaitExpr:
          return FindInExpr(awaitExpr.Operand, line, column);

        case CallExpr callExpr:
          return FindInExpr(callExpr.Callee, line, column)
            ?? FindInExprList(callExpr.Arguments, line, column);

        case GetExpr getExpr:
          return FindInExpr(getExpr.Target, line, column);

        case NullConditionalGetExpr nullConditionalGetExpr:
          return FindInExpr(nullConditionalGetExpr.Target, line, column);

        case IndexExpr indexExpr:
          return FindInExpr(indexExpr.Target, line, column)
            ?? FindInExpr(indexExpr.Index, line, column);

        case NewExpr newExpr:
          return FindInExprList(newExpr.Arguments, line, column);

        case PrefixUpdateExpr prefixUpdateExpr:
          return FindInExpr(prefixUpdateExpr.Operand, line, column);

        case PostfixUpdateExpr postfixUpdateExpr:
          return FindInExpr(postfixUpdateExpr.Operand, line, column);

        case CompoundAssignmentExpr compoundAssignmentExpr:
          return FindInExpr(compoundAssignmentExpr.Target, line, column)
            ?? FindInExpr(compoundAssignmentExpr.Value, line, column);

        case TernaryExpr ternaryExpr:
          return FindInExpr(ternaryExpr.Condition, line, column)
            ?? FindInExpr(ternaryExpr.ThenExpr, line, column)
            ?? FindInExpr(ternaryExpr.ElseExpr, line, column);

        case InterpolatedStringExpr interpolatedStringExpr:
          return FindInExprList(interpolatedStringExpr.Expressions, line, column);

        case TypeTestExpr typeTestExpr:
          return FindInExpr(typeTestExpr.Operand, line, column);

        case TypeCastExpr typeCastExpr:
          return FindInExpr(typeCastExpr.Operand, line, column);

        case CastExpr castExpr:
          return FindInExpr(castExpr.Operand, line, column);

        default:
          return null;
      }
    }

    private static LambdaExpr? FindInExprList(IReadOnlyList<Expr> expressions, int line, int column)
    {
      foreach (var expr in expressions)
      {
        var found = FindInExpr(expr, line, column);
        if (found != null)
        {
          return found;
        }
      }

      return null;
    }
  }
}
