using System.Collections.Generic;

namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>
  /// A constructor declaration: accessModifier? "new" "(" parameters? ")" block.
  /// </summary>
  public class ConstructorDecl : MemberDecl
  {
    public IReadOnlyList<Parameter> Parameters { get; }
    public BlockStmt Body { get; }

    public ConstructorDecl(AccessModifier accessModifier, IReadOnlyList<Parameter> parameters, BlockStmt body)
      : base(accessModifier)
    {
      Parameters = parameters;
      Body = body;
    }
  }
}
