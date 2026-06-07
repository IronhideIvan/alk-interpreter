using System.Collections.Generic;
using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>
  /// Base type for all statement AST nodes. Corresponds to the "statement"
  /// production in the language grammar (§3, §5).
  /// </summary>
  public abstract class Stmt
  {
  }

  /// <summary>An expression used as a statement: expression ";".</summary>
  public class ExpressionStmt : Stmt
  {
    public Expr Expression { get; }

    public ExpressionStmt(Expr expression)
    {
      Expression = expression;
    }
  }

  /// <summary>A block of declarations/statements: "{" declaration* "}".</summary>
  public class BlockStmt : Stmt
  {
    public IReadOnlyList<Stmt> Statements { get; }

    public BlockStmt(IReadOnlyList<Stmt> statements)
    {
      Statements = statements;
    }
  }

  /// <summary>An "if" statement, with an optional "else" branch.</summary>
  public class IfStmt : Stmt
  {
    public Expr Condition { get; }
    public Stmt ThenBranch { get; }
    public Stmt? ElseBranch { get; }

    public IfStmt(Expr condition, Stmt thenBranch, Stmt? elseBranch)
    {
      Condition = condition;
      ThenBranch = thenBranch;
      ElseBranch = elseBranch;
    }
  }

  /// <summary>A "while" loop statement.</summary>
  public class WhileStmt : Stmt
  {
    public Expr Condition { get; }
    public Stmt Body { get; }

    public WhileStmt(Expr condition, Stmt body)
    {
      Condition = condition;
      Body = body;
    }
  }

  /// <summary>
  /// A C-style "for" loop statement. Any of the three clauses may be absent,
  /// matching the grammar's "for" "(" (variableDecl | exprStatement | ";")
  /// expression? ";" expression? ")" statement.
  /// </summary>
  public class ForStmt : Stmt
  {
    public Stmt? Initializer { get; }
    public Expr? Condition { get; }
    public Expr? Increment { get; }
    public Stmt Body { get; }

    public ForStmt(Stmt? initializer, Expr? condition, Expr? increment, Stmt body)
    {
      Initializer = initializer;
      Condition = condition;
      Increment = increment;
      Body = body;
    }
  }

  /// <summary>A "return" statement with an optional expression.</summary>
  public class ReturnStmt : Stmt
  {
    public ALKScriptToken Keyword { get; }
    public Expr? Value { get; }

    public ReturnStmt(ALKScriptToken keyword, Expr? value)
    {
      Keyword = keyword;
      Value = value;
    }
  }

  /// <summary>A "throw" statement: "throw" expression ";".</summary>
  public class ThrowStmt : Stmt
  {
    public ALKScriptToken Keyword { get; }
    public Expr Value { get; }

    public ThrowStmt(ALKScriptToken keyword, Expr value)
    {
      Keyword = keyword;
      Value = value;
    }
  }

  /// <summary>
  /// A single "catch" clause of a "try" statement. The exception type and
  /// binding name are optional, matching "catch" ("(" type IDENTIFIER ")")? block.
  /// </summary>
  public class CatchClause
  {
    public TypeNode? ExceptionType { get; }
    public ALKScriptToken? ExceptionName { get; }
    public BlockStmt Body { get; }

    public CatchClause(TypeNode? exceptionType, ALKScriptToken? exceptionName, BlockStmt body)
    {
      ExceptionType = exceptionType;
      ExceptionName = exceptionName;
      Body = body;
    }
  }

  /// <summary>
  /// A "try" statement: "try" block catchClause* finallyClause?. At least one
  /// catch clause or a finally block must be present.
  /// </summary>
  public class TryStmt : Stmt
  {
    public BlockStmt TryBlock { get; }
    public IReadOnlyList<CatchClause> CatchClauses { get; }
    public BlockStmt? FinallyBlock { get; }

    public TryStmt(BlockStmt tryBlock, IReadOnlyList<CatchClause> catchClauses, BlockStmt? finallyBlock)
    {
      TryBlock = tryBlock;
      CatchClauses = catchClauses;
      FinallyBlock = finallyBlock;
    }
  }
}
