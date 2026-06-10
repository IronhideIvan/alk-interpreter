using System.Collections.Generic;
using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>
  /// A single method signature declared by an <see cref="InterfaceDecl"/>:
  ///   typeParameters? type IDENTIFIER "(" parameters? ")" ";"
  /// Interface methods declare no body and no access/override modifiers —
  /// implementing classes provide both.
  /// </summary>
  public class InterfaceMethodSignature
  {
    public IReadOnlyList<string> TypeParameters { get; }
    public TypeNode ReturnType { get; }
    public ALKScriptToken Name { get; }
    public IReadOnlyList<Parameter> Parameters { get; }

    public InterfaceMethodSignature(IReadOnlyList<string> typeParameters, TypeNode returnType, ALKScriptToken name, IReadOnlyList<Parameter> parameters)
    {
      TypeParameters = typeParameters;
      ReturnType = returnType;
      Name = name;
      Parameters = parameters;
    }
  }

  /// <summary>
  /// An interface declaration:
  ///   "interface" IDENTIFIER typeParameters?
  ///   ( "extends" IDENTIFIER ( "," IDENTIFIER )* )?
  ///   "{" interfaceMethod* "}" ;
  /// </summary>
  public class InterfaceDecl : Decl
  {
    public ALKScriptToken Name { get; }
    public IReadOnlyList<string> TypeParameters { get; }
    public IReadOnlyList<ALKScriptToken> Extends { get; }
    public IReadOnlyList<InterfaceMethodSignature> Methods { get; }

    public InterfaceDecl(
      ALKScriptToken name,
      IReadOnlyList<string> typeParameters,
      IReadOnlyList<ALKScriptToken> extends,
      IReadOnlyList<InterfaceMethodSignature> methods)
    {
      Name = name;
      TypeParameters = typeParameters;
      Extends = extends;
      Methods = methods;
    }
  }
}
