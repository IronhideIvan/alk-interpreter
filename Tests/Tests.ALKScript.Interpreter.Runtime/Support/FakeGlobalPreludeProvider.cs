using System.Collections.Generic;
using ALKScript.Interpreter.Parser.Modules;

namespace Tests.ALKScript.Interpreter.Runtime.Support;

internal class FakeGlobalPreludeProvider : IGlobalPreludeProvider
{
  public FakeGlobalPreludeProvider(params GlobalPreludeSource[] sources)
  {
    Sources = sources;
  }

  public IReadOnlyList<GlobalPreludeSource> Sources { get; }
}
