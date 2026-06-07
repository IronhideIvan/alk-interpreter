using System.Collections.Generic;
using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Common
{
  public interface IScriptParser
  {
    ProgramNode ParseTokens(IEnumerable<ALKScriptToken> tokens);
  }
}
