using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>
  /// A field declaration: accessModifier? "static"? ("var" | type) IDENTIFIER ("=" expression)? ";".
  /// </summary>
  public class FieldDecl : MemberDecl
  {
    /// <summary>The declared type, or null when "var" is used (type is inferred).</summary>
    public TypeNode? Type { get; }

    public ALKScriptToken Name { get; }
    public Expr? Initializer { get; }

    /// <summary>
    /// Whether this field belongs to the class itself (one shared slot, stored
    /// on <see cref="Evaluation.Values.ClassValue.StaticFields"/>) rather than
    /// to each instance.
    /// </summary>
    public bool IsStatic { get; }

    public FieldDecl(AccessModifier accessModifier, TypeNode? type, ALKScriptToken name, Expr? initializer, bool isStatic = false)
      : base(accessModifier)
    {
      Type = type;
      Name = name;
      Initializer = initializer;
      IsStatic = isStatic;
    }
  }
}
