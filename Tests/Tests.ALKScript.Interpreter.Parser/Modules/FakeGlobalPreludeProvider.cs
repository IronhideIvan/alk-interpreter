using System.Collections.Generic;
using ALKScript.Interpreter.Parser.Modules;

namespace Tests.ALKScript.Interpreter.Parser.Modules;

/// <summary>
/// An in-memory <see cref="IGlobalPreludeProvider"/> built directly from a
/// list of <see cref="GlobalPreludeSource"/>s, so tests can describe both
/// "true global" and named-core-module prelude content declaratively.
/// </summary>
public class FakeGlobalPreludeProvider : IGlobalPreludeProvider
{
  public FakeGlobalPreludeProvider(params GlobalPreludeSource[] sources)
  {
    Sources = sources;
  }

  public IReadOnlyList<GlobalPreludeSource> Sources { get; }
}
