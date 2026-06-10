using System.Collections.Generic;
using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>
  /// A single member of an <see cref="EnumDecl"/>:
  ///   IDENTIFIER ( "=" "-"? NUMBER )? ;
  /// <see cref="ExplicitValue"/> is null when the member's value is implicit
  /// (the previous member's value plus one, or zero for the first member).
  /// </summary>
  public class EnumMember
  {
    public ALKScriptToken Name { get; }
    public long? ExplicitValue { get; }

    public EnumMember(ALKScriptToken name, long? explicitValue)
    {
      Name = name;
      ExplicitValue = explicitValue;
    }
  }

  /// <summary>
  /// An enum declaration:
  ///   "enum" IDENTIFIER "{" enumMember ( "," enumMember )* ","? "}" ;
  /// </summary>
  public class EnumDecl : Decl
  {
    public ALKScriptToken Name { get; }
    public IReadOnlyList<EnumMember> Members { get; }

    public EnumDecl(ALKScriptToken name, IReadOnlyList<EnumMember> members)
    {
      Name = name;
      Members = members;
    }
  }
}
