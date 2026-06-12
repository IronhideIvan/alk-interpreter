using System;

namespace ALKScript.Interpreter.Evaluator.Cursor
{
  /// <summary>
  /// Addresses a declaration reachable from a <see cref="ALKScript.Interpreter.Common.Modules.ModuleGraph"/>
  /// without serializing the AST itself — used by the "Phase B" structural
  /// Capture/Restore design (docs/ASYNC_AWAIT_DESIGN.md Addendum 3) to
  /// reference <c>ClassValue</c>/<c>FunctionValue</c>/<c>InterfaceValue</c>/
  /// <c>EnumTypeValue</c>/<c>EnumValue</c> instances by where their
  /// declaration lives rather than by serializing the declaration.
  ///
  /// <see cref="ModuleKey"/> is <c>"module:&lt;identifier&gt;"</c> (for
  /// <see cref="ALKScript.Interpreter.Common.Modules.ModuleGraph.Modules"/>)
  /// or <c>"prelude:&lt;index&gt;"</c> (for
  /// <see cref="ALKScript.Interpreter.Common.Modules.ModuleGraph.GlobalPreludes"/>).
  ///
  /// <see cref="Path"/> is a dotted path through top-level declarations and
  /// class members, e.g. <c>"Animal"</c>, <c>"Animal.speak"</c>,
  /// <c>"Animal.&lt;ctor&gt;"</c>, <c>"Color.Red"</c>,
  /// <c>"&lt;lambda&gt;@12:8"</c>, <c>"Animal.speak.&lt;lambda&gt;@14:10"</c>.
  /// </summary>
  public sealed class AstReference : IEquatable<AstReference>
  {
    public string ModuleKey { get; }

    public string Path { get; }

    public AstReference(string moduleKey, string path)
    {
      ModuleKey = moduleKey;
      Path = path;
    }

    public static string ForModule(string moduleIdentifier) => "module:" + moduleIdentifier;

    public static string ForPrelude(int index) => "prelude:" + index;

    public bool Equals(AstReference? other) =>
      other != null && ModuleKey == other.ModuleKey && Path == other.Path;

    public override bool Equals(object? obj) => obj is AstReference other && Equals(other);

    public override int GetHashCode() => (ModuleKey, Path).GetHashCode();

    public override string ToString() => $"{ModuleKey}#{Path}";
  }
}
