using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Evaluator
{
  /// <summary>
  /// Locates the token an AST node should be reported against when raising a
  /// <see cref="RuntimeException"/> — e.g. the statement's leading keyword/name,
  /// or the expression's operator/closing-delimiter.
  /// </summary>
  internal static class AstTokenLocator
  {
    public static ALKScriptToken Of(Stmt statement)
    {
      switch (statement)
      {
        case ReturnStmt returnStmt:
          return returnStmt.Keyword;
        case ThrowStmt throwStmt:
          return throwStmt.Keyword;
        case VariableDecl variableDecl:
          return variableDecl.Name;
        case FunctionDecl functionDecl:
          return functionDecl.Name;
        case ClassDecl classDecl:
          return classDecl.Name;
        case EnumDecl enumDecl:
          return enumDecl.Name;
        case InterfaceDecl interfaceDecl:
          return interfaceDecl.Name;
        case ForeachStmt foreachStmt:
          return foreachStmt.Keyword;
        case DoWhileStmt doWhileStmt:
          return doWhileStmt.Keyword;
        case SwitchStmt switchStmt:
          return switchStmt.Keyword;
        default:
          return EndOfFile;
      }
    }

    public static ALKScriptToken Of(Expr expression)
    {
      switch (expression)
      {
        case LiteralExpr literal:
          return literal.Token;
        case IdentifierExpr identifier:
          return identifier.Name;
        case ThisExpr thisExpr:
          return thisExpr.Keyword;
        case BaseExpr baseExpr:
          return baseExpr.Keyword;
        case AssignmentExpr assignment:
          return Of(assignment.Target);
        case BinaryExpr binary:
          return binary.Operator;
        case UnaryExpr unary:
          return unary.Operator;
        case CallExpr call:
          return call.ClosingParen;
        case GetExpr get:
          return get.Name;
        case IndexExpr index:
          return index.ClosingBracket;
        case NewExpr newExpr:
          return newExpr.Keyword;
        case AwaitExpr awaitExpr:
          return awaitExpr.Keyword;
        case PrefixUpdateExpr prefixUpdate:
          return prefixUpdate.Operator;
        case PostfixUpdateExpr postfixUpdate:
          return postfixUpdate.Operator;
        case CompoundAssignmentExpr compound:
          return compound.Operator;
        case TernaryExpr ternary:
          return ternary.QuestionToken;
        case NullConditionalGetExpr nullCondGet:
          return nullCondGet.Name;
        case InterpolatedStringExpr interpolated:
          return interpolated.Token;
        case TypeTestExpr typeTest:
          return typeTest.Keyword;
        case TypeCastExpr typeCast:
          return typeCast.Keyword;
        case CastExpr cast:
          return cast.Keyword;
        case LambdaExpr lambda:
          return lambda.Arrow;
        default:
          return EndOfFile;
      }
    }

    public static readonly ALKScriptToken EndOfFile = new ALKScriptToken(ALKScriptTokenType.EndOfFile, string.Empty, 0, 0);
  }
}
