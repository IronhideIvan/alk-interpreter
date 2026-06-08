using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Common.Ast
{
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
}
