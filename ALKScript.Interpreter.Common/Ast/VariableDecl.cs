using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>
  /// A variable declaration: ("var" | type) IDENTIFIER ("=" expression)? ";".
  /// "var" requires an initializer (the type is inferred); an explicit type
  /// makes the initializer optional.
  /// </summary>
  public class VariableDecl : Decl
  {
    /// <summary>The declared type, or null when "var" is used (type is inferred).</summary>
    public TypeNode? Type { get; }

    public ALKScriptToken Name { get; }
    public Expr? Initializer { get; }

    public VariableDecl(TypeNode? type, ALKScriptToken name, Expr? initializer)
    {
      Type = type;
      Name = name;
      Initializer = initializer;
    }
  }
}
