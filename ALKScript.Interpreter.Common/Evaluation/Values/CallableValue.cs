namespace ALKScript.Interpreter.Common.Evaluation.Values
{
  /// <summary>Base type for any value that can appear as the callee of a <see cref="Ast.CallExpr"/>.</summary>
  public abstract class CallableValue : ALKScriptValue
  {
    /// <summary>The number of arguments this callable expects.</summary>
    public abstract int Arity { get; }
  }
}
