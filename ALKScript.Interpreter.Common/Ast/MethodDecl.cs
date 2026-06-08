using System.Collections.Generic;
using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>
  /// A method declaration:
  ///   accessModifier? overrideModifier? "native"? "async"? "function" typeParameters?
  ///   type IDENTIFIER "(" parameters? ")" ( block | ";" ) ;
  /// The body is null for "abstract" methods and for "native" methods —
  /// the latter's implementation is supplied by the host runtime rather
  /// than ALKScript source, and its declaration ends with ";" accordingly.
  /// </summary>
  public class MethodDecl : MemberDecl
  {
    public OverrideModifier OverrideModifier { get; }
    public bool IsNative { get; }
    public bool IsAsync { get; }
    public IReadOnlyList<string> TypeParameters { get; }
    public TypeNode ReturnType { get; }
    public ALKScriptToken Name { get; }
    public IReadOnlyList<Parameter> Parameters { get; }
    public BlockStmt? Body { get; }

    public MethodDecl(
      AccessModifier accessModifier,
      OverrideModifier overrideModifier,
      bool isNative,
      bool isAsync,
      IReadOnlyList<string> typeParameters,
      TypeNode returnType,
      ALKScriptToken name,
      IReadOnlyList<Parameter> parameters,
      BlockStmt? body)
      : base(accessModifier)
    {
      OverrideModifier = overrideModifier;
      IsNative = isNative;
      IsAsync = isAsync;
      TypeParameters = typeParameters;
      ReturnType = returnType;
      Name = name;
      Parameters = parameters;
      Body = body;
    }
  }
}
