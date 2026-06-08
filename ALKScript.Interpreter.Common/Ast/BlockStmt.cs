using System.Collections.Generic;

namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>A block of declarations/statements: "{" declaration* "}".</summary>
  public class BlockStmt : Stmt
  {
    public IReadOnlyList<Stmt> Statements { get; }

    public BlockStmt(IReadOnlyList<Stmt> statements)
    {
      Statements = statements;
    }
  }
}
