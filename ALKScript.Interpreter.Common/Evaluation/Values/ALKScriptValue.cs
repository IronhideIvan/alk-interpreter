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

  /// <summary>The single "null" value. Reference-comparable via <see cref="Instance"/>.</summary>
  public sealed class NullValue : ALKScriptValue
  {
    public static readonly NullValue Instance = new NullValue();

    private NullValue()
    {
    }

    public override string TypeName => "null";

    public override bool IsTruthy => false;

    public override string ToString() => "null";
  }

  /// <summary>A boolean value. Interned via <see cref="True"/>/<see cref="False"/>.</summary>
  public sealed class BoolValue : ALKScriptValue
  {
    public static readonly BoolValue True = new BoolValue(true);
    public static readonly BoolValue False = new BoolValue(false);

    public bool Value { get; }

    private BoolValue(bool value)
    {
      Value = value;
    }

    public static BoolValue Of(bool value) => value ? True : False;

    public override string TypeName => "bool";

    public override bool IsTruthy => Value;

    public override string ToString() => Value ? "true" : "false";
  }

  /// <summary>An integer value, backed by a 64-bit signed integer (ALKScript's "int"/"long").</summary>
  public sealed class IntValue : ALKScriptValue
  {
    public long Value { get; }

    public IntValue(long value)
    {
      Value = value;
    }

    public override string TypeName => "int";

    public override string ToString() => Value.ToString();
  }

  /// <summary>A floating-point value, backed by a double (ALKScript's "float").</summary>
  public sealed class FloatValue : ALKScriptValue
  {
    public double Value { get; }

    public FloatValue(double value)
    {
      Value = value;
    }

    public override string TypeName => "float";

    public override string ToString() => Value.ToString();
  }

  /// <summary>A string value.</summary>
  public sealed class StringValue : ALKScriptValue
  {
    public string Value { get; }

    public StringValue(string value)
    {
      Value = value;
    }

    public override string TypeName => "string";

    public override string ToString() => Value;
  }
}
