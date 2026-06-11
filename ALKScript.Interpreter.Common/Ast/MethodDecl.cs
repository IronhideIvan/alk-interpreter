using System.Collections.Generic;
using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>
  /// A method declaration:
  ///   accessModifier? "static"? overrideModifier? "native"? "function" typeParameters?
  ///   type IDENTIFIER "(" parameters? ")" ( block | ";" ) ;
  /// The body is null for "abstract" methods and for "native" methods —
  /// the latter's implementation is supplied by the host runtime rather
  /// than ALKScript source, and its declaration ends with ";" accordingly.
  /// "static" is mutually exclusive with a non-"None" <see cref="OverrideModifier"/> —
  /// a static method cannot be virtual, abstract, or an override.
  /// </summary>
  public class MethodDecl : MemberDecl
  {
    public OverrideModifier OverrideModifier { get; }
    public bool IsNative { get; }

    /// <summary>
    /// Whether this method belongs to the class itself (called as
    /// "ClassName.method(...)", with no bound instance/"this") rather than
    /// to each instance.
    /// </summary>
    public bool IsStatic { get; }

    public IReadOnlyList<string> TypeParameters { get; }
    public TypeNode ReturnType { get; }
    public ALKScriptToken Name { get; }
    public IReadOnlyList<Parameter> Parameters { get; }
    public BlockStmt? Body { get; }

    public MethodDecl(
      AccessModifier accessModifier,
      OverrideModifier overrideModifier,
      bool isNative,
      IReadOnlyList<string> typeParameters,
      TypeNode returnType,
      ALKScriptToken name,
      IReadOnlyList<Parameter> parameters,
      BlockStmt? body,
      bool isStatic = false)
      : base(accessModifier)
    {
      OverrideModifier = overrideModifier;
      IsNative = isNative;
      IsStatic = isStatic;
      TypeParameters = typeParameters;
      ReturnType = returnType;
      Name = name;
      Parameters = parameters;
      Body = body;
    }
  }
}
