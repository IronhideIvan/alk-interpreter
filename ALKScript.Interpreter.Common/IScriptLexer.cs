using System.Collections.Generic;

namespace ALKScript.Interpreter.Common
{
  public interface IScriptLexer
  {
    IEnumerable<ALKScriptToken> Tokenize(string contents);
  }
}
