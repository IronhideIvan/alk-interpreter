using System.Collections.Generic;
using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Common.Evaluation;

namespace ALKScript.Interpreter.Evaluator
{
  /// <summary>
  /// Executes statements: dispatches on <see cref="Stmt"/> shape and drives
  /// control flow (blocks, conditionals, loops, "return"/"throw"/"try").
  /// </summary>
  internal interface IStatementExecutor
  {
    void Execute(Stmt statement, ScriptEnvironment environment);

    void ExecuteBlock(IReadOnlyList<Stmt> statements, ScriptEnvironment environment);
  }
}
