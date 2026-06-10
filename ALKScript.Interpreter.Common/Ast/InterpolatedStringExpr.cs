using System.Collections.Generic;
using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>
  /// A template-literal string with interpolated expressions, e.g. `Hello ${name}!`.
  ///
  /// <para>
  /// <see cref="Parts"/> contains the literal text segments, and <see cref="Expressions"/> contains
  /// the interpolated expressions between them. There is always exactly one more part than there
  /// are expressions, i.e. Parts[0] + Expressions[0] + Parts[1] + Expressions[1] + ... + Parts[N].
  /// </para>
  /// </summary>
  public class InterpolatedStringExpr : Expr
  {
    public ALKScriptToken Token { get; }
    public IReadOnlyList<string> Parts { get; }
    public IReadOnlyList<Expr> Expressions { get; }

    public InterpolatedStringExpr(ALKScriptToken token, IReadOnlyList<string> parts, IReadOnlyList<Expr> expressions)
    {
      Token = token;
      Parts = parts;
      Expressions = expressions;
    }
  }
}
