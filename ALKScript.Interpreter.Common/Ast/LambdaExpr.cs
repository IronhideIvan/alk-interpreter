using System.Collections.Generic;
using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>
  /// A lambda expression: <c>type "(" parameters? ")" "=&gt;" block</c>.
  /// Mirrors a <see cref="FunctionDecl"/> but is anonymous and evaluates to a
  /// closure capturing the defining environment (and, when written inside a
  /// method body, the enclosing "this"/"base").
  /// </summary>
  public class LambdaExpr : Expr
  {
    /// <summary>The "=&gt;" token, used for error reporting.</summary>
    public ALKScriptToken Arrow { get; }

    public TypeNode ReturnType { get; }
    public IReadOnlyList<Parameter> Parameters { get; }
    public BlockStmt Body { get; }

    public LambdaExpr(ALKScriptToken arrow, TypeNode returnType, IReadOnlyList<Parameter> parameters, BlockStmt body)
    {
      Arrow = arrow;
      ReturnType = returnType;
      Parameters = parameters;
      Body = body;
    }
  }
}
