using System.Collections.Generic;
using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>
  /// A class declaration:
  ///   "native"? "abstract"? "class" IDENTIFIER typeParameters?
  ///   ( "extends" IDENTIFIER ( "&lt;" type ("," type)* "&gt;" )? )?
  ///   "{" member* "}" ;
  ///
  /// A class must be declared "native" if any of its members are themselves
  /// "native" — see the parser's validation in <c>ParseClassDecl</c> and the
  /// "native class" section of the language specification.
  /// </summary>
  public class ClassDecl : Decl
  {
    public bool IsAbstract { get; }
    public bool IsNative { get; }
    public ALKScriptToken Name { get; }
    public IReadOnlyList<string> TypeParameters { get; }
    public ALKScriptToken? SuperclassName { get; }
    public IReadOnlyList<TypeNode> SuperclassTypeArguments { get; }
    public IReadOnlyList<MemberDecl> Members { get; }

    public ClassDecl(
      bool isAbstract,
      ALKScriptToken name,
      IReadOnlyList<string> typeParameters,
      ALKScriptToken? superclassName,
      IReadOnlyList<TypeNode> superclassTypeArguments,
      IReadOnlyList<MemberDecl> members,
      bool isNative = false)
    {
      IsAbstract = isAbstract;
      IsNative = isNative;
      Name = name;
      TypeParameters = typeParameters;
      SuperclassName = superclassName;
      SuperclassTypeArguments = superclassTypeArguments;
      Members = members;
    }
  }
}
