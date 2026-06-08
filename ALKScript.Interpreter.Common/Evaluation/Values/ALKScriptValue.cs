namespace ALKScript.Interpreter.Common.Evaluation.Values
{
  /// <summary>
  /// Base type for every runtime value the evaluator produces or operates on.
  /// Mirrors the AST's "one base type per family" style (<see cref="Ast.Stmt"/>,
  /// <see cref="Ast.Expr"/>): a small closed hierarchy of dedicated value types
  /// rather than boxed CLR objects, so the evaluator can dispatch on type,
  /// and so equality/truthiness/string-conversion have one obvious home.
  /// </summary>
  public abstract class ALKScriptValue
  {
    /// <summary>The ALKScript type name as it would appear in source/diagnostics, e.g. "int" or "string".</summary>
    public abstract string TypeName { get; }

    /// <summary>Whether this value is "truthy" when used as a condition (e.g. in "if"/"while").</summary>
    public virtual bool IsTruthy => true;
  }
}
