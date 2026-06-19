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
  /// A property signature declared in an interface:
  ///   "property" type IDENTIFIER "{" "get" ";" ( "set" ";" )? "}"
  /// </summary>
  public class InterfacePropertySignature
  {
    public TypeNode Type { get; }
    public ALKScriptToken Name { get; }
    public bool HasGetter { get; }
    public bool HasSetter { get; }

    public InterfacePropertySignature(TypeNode type, ALKScriptToken name, bool hasGetter, bool hasSetter)
    {
      Type = type;
      Name = name;
      HasGetter = hasGetter;
      HasSetter = hasSetter;
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
    public IReadOnlyList<InterfacePropertySignature> Properties { get; }

    public InterfaceDecl(
      ALKScriptToken name,
      IReadOnlyList<string> typeParameters,
      IReadOnlyList<ALKScriptToken> extends,
      IReadOnlyList<InterfaceMethodSignature> methods,
      IReadOnlyList<InterfacePropertySignature>? properties = null)
    {
      Name = name;
      TypeParameters = typeParameters;
      Extends = extends;
      Methods = methods;
      Properties = properties ?? System.Array.Empty<InterfacePropertySignature>();
    }
  }
}
