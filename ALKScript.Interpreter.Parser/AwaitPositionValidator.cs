using ALKScript.Interpreter.Common.Ast;

namespace ALKScript.Interpreter.Parser
{
  /// <summary>
  /// Enforces LANGUAGE_SPEC.md §8.1: <c>await</c> (including <c>await [a, b, ...]</c>)
  /// may only appear as the entire initializer of a <c>var</c> declaration, the
  /// entire value of a <c>return</c>/<c>throw</c> statement, or the entire
  /// expression of an expression statement. An <c>await</c> appearing in any
  /// other position — as a binary operand, call argument, array element, field
  /// initializer, etc. — is a parse-time error.
  /// </summary>
  internal static class AwaitPositionValidator
  {
    public static void Validate(ProgramNode program)
    {
      foreach (var declaration in program.Declarations)
      {
        ValidateStmt(declaration);
      }
    }

    private static void ValidateStmt(Stmt stmt)
    {
      switch (stmt)
      {
        case ExpressionStmt expressionStmt:
          ValidateAwaitAllowed(expressionStmt.Expression);
          break;

        case VariableDecl variableDecl:
          if (variableDecl.Initializer != null) ValidateAwaitAllowed(variableDecl.Initializer);
          break;

        case ReturnStmt returnStmt:
          if (returnStmt.Value != null) ValidateAwaitAllowed(returnStmt.Value);
          break;

        case ThrowStmt throwStmt:
          ValidateAwaitAllowed(throwStmt.Value);
          break;

        case BlockStmt blockStmt:
          foreach (var inner in blockStmt.Statements) ValidateStmt(inner);
          break;

        case IfStmt ifStmt:
          ValidateNoAwait(ifStmt.Condition);
          ValidateStmt(ifStmt.ThenBranch);
          if (ifStmt.ElseBranch != null) ValidateStmt(ifStmt.ElseBranch);
          break;

        case WhileStmt whileStmt:
          ValidateNoAwait(whileStmt.Condition);
          ValidateStmt(whileStmt.Body);
          break;

        case DoWhileStmt doWhileStmt:
          ValidateStmt(doWhileStmt.Body);
          ValidateNoAwait(doWhileStmt.Condition);
          break;

        case ForStmt forStmt:
          if (forStmt.Initializer != null) ValidateStmt(forStmt.Initializer);
          if (forStmt.Condition != null) ValidateNoAwait(forStmt.Condition);
          if (forStmt.Increment != null) ValidateNoAwait(forStmt.Increment);
          ValidateStmt(forStmt.Body);
          break;

        case ForeachStmt foreachStmt:
          ValidateNoAwait(foreachStmt.Collection);
          ValidateStmt(foreachStmt.Body);
          break;

        case TryStmt tryStmt:
          ValidateStmt(tryStmt.TryBlock);
          foreach (var catchClause in tryStmt.CatchClauses) ValidateStmt(catchClause.Body);
          if (tryStmt.FinallyBlock != null) ValidateStmt(tryStmt.FinallyBlock);
          break;

        case SwitchStmt switchStmt:
          ValidateNoAwait(switchStmt.Discriminant);
          foreach (var switchCase in switchStmt.Cases)
          {
            if (switchCase.Test != null) ValidateNoAwait(switchCase.Test);
            foreach (var inner in switchCase.Body) ValidateStmt(inner);
          }
          break;

        case FunctionDecl functionDecl:
          if (functionDecl.Body != null) ValidateStmt(functionDecl.Body);
          break;

        case ClassDecl classDecl:
          foreach (var member in classDecl.Members) ValidateMember(member);
          break;

        case ExportDecl exportDecl:
          ValidateStmt(exportDecl.Declaration);
          break;

        case StatementDecl statementDecl:
          ValidateStmt(statementDecl.Statement);
          break;

        default:
          // BreakStmt, ContinueStmt, EnumDecl, InterfaceDecl, ReExportDecl: no expressions.
          break;
      }
    }

    private static void ValidateMember(MemberDecl member)
    {
      switch (member)
      {
        case FieldDecl fieldDecl:
          if (fieldDecl.Initializer != null) ValidateNoAwait(fieldDecl.Initializer);
          break;

        case MethodDecl methodDecl:
          if (methodDecl.Body != null) ValidateStmt(methodDecl.Body);
          break;

        case ConstructorDecl constructorDecl:
          ValidateStmt(constructorDecl.Body);
          break;
      }
    }

    /// <summary>
    /// Validates an expression appearing in one of the four allowed positions
    /// (the entire initializer/return/throw/expression-statement value): an
    /// <see cref="AwaitExpr"/> here is allowed, but its operand (e.g. the
    /// elements of an <c>await [a, b, ...]</c> array) must not itself contain
    /// another <c>await</c>.
    /// </summary>
    private static void ValidateAwaitAllowed(Expr expr)
    {
      if (expr is AwaitExpr awaitExpr)
      {
        ValidateNoAwait(awaitExpr.Operand);
      }
      else
      {
        ValidateNoAwait(expr);
      }
    }

    /// <summary>Throws a <see cref="ParseException"/> if <paramref name="expr"/> contains an <c>await</c> anywhere.</summary>
    private static void ValidateNoAwait(Expr expr)
    {
      switch (expr)
      {
        case AwaitExpr awaitExpr:
          throw new ParseException(awaitExpr.Keyword,
            "'await' is only allowed as the entire initializer of a 'var' declaration, the entire " +
            "value of a 'return'/'throw' statement, or the entire expression of an expression " +
            "statement (optionally as 'await [a, b, ...]' in one of those positions) — see " +
            "LANGUAGE_SPEC.md §8.1. Rewrite as 'var t = await ...;' first.");

        case GroupingExpr groupingExpr:
          ValidateNoAwait(groupingExpr.Expression);
          break;

        case ArrayLiteralExpr arrayLiteralExpr:
          foreach (var element in arrayLiteralExpr.Elements) ValidateNoAwait(element);
          break;

        case AssignmentExpr assignmentExpr:
          ValidateNoAwait(assignmentExpr.Target);
          ValidateNoAwait(assignmentExpr.Value);
          break;

        case BinaryExpr binaryExpr:
          ValidateNoAwait(binaryExpr.Left);
          ValidateNoAwait(binaryExpr.Right);
          break;

        case UnaryExpr unaryExpr:
          ValidateNoAwait(unaryExpr.Operand);
          break;

        case CallExpr callExpr:
          ValidateNoAwait(callExpr.Callee);
          foreach (var argument in callExpr.Arguments) ValidateNoAwait(argument);
          break;

        case GetExpr getExpr:
          ValidateNoAwait(getExpr.Target);
          break;

        case NullConditionalGetExpr nullConditionalGetExpr:
          ValidateNoAwait(nullConditionalGetExpr.Target);
          break;

        case IndexExpr indexExpr:
          ValidateNoAwait(indexExpr.Target);
          ValidateNoAwait(indexExpr.Index);
          break;

        case NewExpr newExpr:
          foreach (var argument in newExpr.Arguments) ValidateNoAwait(argument);
          break;

        case MapLiteralExpr mapLiteralExpr:
          foreach (var (k, v) in mapLiteralExpr.Entries) { ValidateNoAwait(k); ValidateNoAwait(v); }
          break;

        case PrefixUpdateExpr prefixUpdateExpr:
          ValidateNoAwait(prefixUpdateExpr.Operand);
          break;

        case PostfixUpdateExpr postfixUpdateExpr:
          ValidateNoAwait(postfixUpdateExpr.Operand);
          break;

        case CompoundAssignmentExpr compoundAssignmentExpr:
          ValidateNoAwait(compoundAssignmentExpr.Target);
          ValidateNoAwait(compoundAssignmentExpr.Value);
          break;

        case TernaryExpr ternaryExpr:
          ValidateNoAwait(ternaryExpr.Condition);
          ValidateNoAwait(ternaryExpr.ThenExpr);
          ValidateNoAwait(ternaryExpr.ElseExpr);
          break;

        case InterpolatedStringExpr interpolatedStringExpr:
          foreach (var part in interpolatedStringExpr.Expressions) ValidateNoAwait(part);
          break;

        case TypeTestExpr typeTestExpr:
          ValidateNoAwait(typeTestExpr.Operand);
          break;

        case TypeCastExpr typeCastExpr:
          ValidateNoAwait(typeCastExpr.Operand);
          break;

        case CastExpr castExpr:
          ValidateNoAwait(castExpr.Operand);
          break;

        case LambdaExpr lambdaExpr:
          ValidateStmt(lambdaExpr.Body);
          break;

        default:
          // LiteralExpr, IdentifierExpr, ThisExpr, BaseExpr: no sub-expressions.
          break;
      }
    }
  }
}
