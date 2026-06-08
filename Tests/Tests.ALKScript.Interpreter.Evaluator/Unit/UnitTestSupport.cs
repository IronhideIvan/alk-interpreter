using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Common.Token;

namespace Tests.ALKScript.Interpreter.Evaluator.Unit;

/// <summary>
/// Builders for the small AST/token fragments the unit tests below construct
/// by hand (rather than via the lexer/parser) to exercise the evaluator's
/// internal collaborators in isolation.
/// </summary>
internal static class Nodes
{
  public static ALKScriptToken Token(ALKScriptTokenType type, string lexeme = "") => new ALKScriptToken(type, lexeme, 1, 1);

  public static ALKScriptToken Identifier(string name) => Token(ALKScriptTokenType.Identifier, name);

  public static ALKScriptToken Operator(ALKScriptTokenType type, string lexeme) => Token(type, lexeme);

  public static LiteralExpr Literal(object? value) => new LiteralExpr(Token(ALKScriptTokenType.Number, value?.ToString() ?? "null"), value);

  public static IdentifierExpr Ident(string name) => new IdentifierExpr(Identifier(name));

  public static TypeNode VoidType => new TypeNode("void", System.Array.Empty<TypeNode>(), 0, false);
}
