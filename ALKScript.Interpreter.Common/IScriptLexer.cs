using System.Collections.Generic;
using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Common
{
  public interface IScriptLexer
  {
    IEnumerable<ALKScriptToken> Tokenize(string contents);
  }
}
