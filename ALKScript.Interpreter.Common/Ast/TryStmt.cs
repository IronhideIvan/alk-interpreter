using System.Collections.Generic;

namespace ALKScript.Interpreter.Common.Ast
{
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
