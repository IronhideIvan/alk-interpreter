using ALKScript.Interpreter.Common.Ast;

namespace ALKScript.Interpreter.Common
{
  public interface IScriptParser
  {
    ProgramNode ParseProgram();
  }
}
