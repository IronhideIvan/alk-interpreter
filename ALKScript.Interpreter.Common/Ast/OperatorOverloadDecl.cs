using System.Collections.Generic;
using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>
  /// An operator overload declaration:
  ///   accessModifier? "static" "operator" returnType op "(" params ")" block
  /// Operator methods must be static. The "operator" keyword appears where a
  /// method name normally would.
  /// </summary>
  public class OperatorOverloadDecl : MemberDecl
  {
    /// <summary>The operator token (e.g. "+", "-", "==", etc.).</summary>
    public ALKScriptToken Operator { get; }
    public TypeNode ReturnType { get; }
    public IReadOnlyList<Parameter> Parameters { get; }
    public BlockStmt Body { get; }

    public OperatorOverloadDecl(
      AccessModifier accessModifier,
      ALKScriptToken @operator,
      TypeNode returnType,
      IReadOnlyList<Parameter> parameters,
      BlockStmt body)
      : base(accessModifier)
    {
      Operator = @operator;
      ReturnType = returnType;
      Parameters = parameters;
      Body = body;
    }

    public bool IsUnary => Parameters.Count == 1;
    public bool IsBinary => Parameters.Count == 2;
  }
}
