using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Common.Evaluation;
using ALKScript.Interpreter.Common.Modules;

namespace ALKScript.Interpreter.Evaluator
{
  /// <summary>
  /// Tree-walking evaluator that executes a <see cref="ModuleGraph"/> by
  /// running the entry module's top-level declarations and statements.
  /// </summary>
  public class ProgramEvaluator : IEvaluator
  {
    public void Evaluate(ModuleGraph graph)
    {
      var globals = new Environment();

      ExecuteModule(graph.EntryModule, globals);
    }

    private void ExecuteModule(LoadedModule module, Environment environment)
    {
      foreach (var declaration in module.Program.Declarations)
      {
        ExecuteStatement(declaration, environment);
      }
    }

    private void ExecuteStatement(Stmt statement, Environment environment)
    {
      throw new System.NotImplementedException(
        $"Evaluation of '{statement.GetType().Name}' is not yet implemented.");
    }
  }
}
