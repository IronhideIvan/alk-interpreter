using System.Collections.Generic;
using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>
  /// A class declaration:
  ///   "abstract"? "class" IDENTIFIER typeParameters?
  ///   ( "extends" IDENTIFIER ( "&lt;" type ("," type)* "&gt;" )? )?
  ///   "{" member* "}" ;
  /// </summary>
  public class ClassDecl : Decl
  {
    public bool IsAbstract { get; }
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
      IReadOnlyList<MemberDecl> members)
    {
      IsAbstract = isAbstract;
      Name = name;
      TypeParameters = typeParameters;
      SuperclassName = superclassName;
      SuperclassTypeArguments = superclassTypeArguments;
      Members = members;
    }
  }
}
