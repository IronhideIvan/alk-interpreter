using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>
  /// A variable declaration: "const"? ("var" | type) IDENTIFIER ("=" expression)? ";".
  /// "var" requires an initializer (the type is inferred); an explicit type
  /// makes the initializer optional. "const" requires an initializer
  /// regardless, and forbids any later assignment to the name.
  /// </summary>
  public class VariableDecl : Decl
  {
    /// <summary>The declared type, or null when "var" is used (type is inferred).</summary>
    public TypeNode? Type { get; }

    public ALKScriptToken Name { get; }
    public Expr? Initializer { get; }

    /// <summary>True when declared with "const"; the binding cannot be reassigned.</summary>
    public bool IsConst { get; }

    public VariableDecl(TypeNode? type, ALKScriptToken name, Expr? initializer, bool isConst = false)
    {
      Type = type;
      Name = name;
      Initializer = initializer;
      IsConst = isConst;
    }
  }
}
