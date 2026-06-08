using System.Collections.Generic;
using System.Threading.Tasks;
using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Common.Evaluation;

namespace ALKScript.Interpreter.Evaluator
{
  /// <summary>
  /// Executes statements: dispatches on <see cref="Stmt"/> shape and drives
  /// control flow (blocks, conditionals, loops, "return"/"throw"/"try").
  /// <see cref="Task"/>-returning so that an <c>await</c> anywhere within a
  /// statement (or nested block/loop/try) can suspend execution and resume
  /// later without losing in-flight control-flow state.
  /// </summary>
  internal interface IStatementExecutor
  {
    Task Execute(Stmt statement, ScriptEnvironment environment);

    Task ExecuteBlock(IReadOnlyList<Stmt> statements, ScriptEnvironment environment);
  }
}
