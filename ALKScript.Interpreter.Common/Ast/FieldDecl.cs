using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>
  /// A field declaration: accessModifier? ("var" | type) IDENTIFIER ("=" expression)? ";".
  /// </summary>
  public class FieldDecl : MemberDecl
  {
    /// <summary>The declared type, or null when "var" is used (type is inferred).</summary>
    public TypeNode? Type { get; }

    public ALKScriptToken Name { get; }
    public Expr? Initializer { get; }

    public FieldDecl(AccessModifier accessModifier, TypeNode? type, ALKScriptToken name, Expr? initializer)
      : base(accessModifier)
    {
      Type = type;
      Name = name;
      Initializer = initializer;
    }
  }
}
