using System.Collections.Generic;
using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>
  /// A top-level function declaration:
  ///   "native"? "async"? "function" typeParameters? type IDENTIFIER "(" parameters? ")" ( block | ";" ) ;
  /// "native" marks a declaration whose implementation is supplied by the
  /// host runtime rather than ALKScript source; such declarations end with
  /// ";" and have a null <see cref="Body"/>, mirroring how abstract methods
  /// are declared without one.
  /// </summary>
  public class FunctionDecl : Decl
  {
    public bool IsNative { get; }
    public bool IsAsync { get; }
    public IReadOnlyList<string> TypeParameters { get; }
    public TypeNode ReturnType { get; }
    public ALKScriptToken Name { get; }
    public IReadOnlyList<Parameter> Parameters { get; }

    /// <summary>The function's body, or null when <see cref="IsNative"/> is true.</summary>
    public BlockStmt? Body { get; }

    public FunctionDecl(
      bool isNative,
      bool isAsync,
      IReadOnlyList<string> typeParameters,
      TypeNode returnType,
      ALKScriptToken name,
      IReadOnlyList<Parameter> parameters,
      BlockStmt? body)
    {
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
