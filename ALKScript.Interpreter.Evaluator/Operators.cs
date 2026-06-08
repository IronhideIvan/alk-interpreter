using System;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Evaluator
{
  /// <summary>
  /// Stateless arithmetic, comparison and equality semantics for
  /// <see cref="ALKScriptValue"/>s, shared by binary and unary expression
  /// evaluation.
  /// </summary>
  internal static class Operators
  {
    public static ALKScriptValue Add(ALKScriptValue left, ALKScriptValue right, ALKScriptToken op)
    {
      // String concatenation when either operand is a string; numeric addition otherwise.
      if (left is StringValue || right is StringValue)
      {
        return new StringValue(Stringify(left) + Stringify(right));
      }

      return Arithmetic(left, right, op, (a, b) => a + b, (a, b) => a + b);
    }

    public static ALKScriptValue Arithmetic(
      ALKScriptValue left,
      ALKScriptValue right,
      ALKScriptToken op,
      Func<long, long, long> onInts,
      Func<double, double, double> onFloats)
    {
      if (left is IntValue leftInt && right is IntValue rightInt)
      {
        return new IntValue(onInts(leftInt.Value, rightInt.Value));
      }

      if (TryToNumber(left, out double leftNumber) && TryToNumber(right, out double rightNumber))
      {
        return new FloatValue(onFloats(leftNumber, rightNumber));
      }

      throw new RuntimeException(op, $"Operator '{op.Lexeme}' cannot be applied to '{left.TypeName}' and '{right.TypeName}'.");
    }

    public static int Compare(ALKScriptValue left, ALKScriptValue right, ALKScriptToken op)
    {
      if (left is StringValue leftString && right is StringValue rightString)
      {
        return string.CompareOrdinal(leftString.Value, rightString.Value);
      }

      if (TryToNumber(left, out double leftNumber) && TryToNumber(right, out double rightNumber))
      {
        return leftNumber.CompareTo(rightNumber);
      }

      throw new RuntimeException(op, $"Operator '{op.Lexeme}' cannot be applied to '{left.TypeName}' and '{right.TypeName}'.");
    }

    public static bool AreEqual(ALKScriptValue left, ALKScriptValue right)
    {
      switch (left)
      {
        case NullValue _:
          return right is NullValue;
        case BoolValue leftBool when right is BoolValue rightBool:
          return leftBool.Value == rightBool.Value;
        case StringValue leftString when right is StringValue rightString:
          return leftString.Value == rightString.Value;
        case IntValue leftInt when right is IntValue rightInt:
          return leftInt.Value == rightInt.Value;
        default:
          return TryToNumber(left, out double leftNumber)
            && TryToNumber(right, out double rightNumber)
            && leftNumber == rightNumber;
      }
    }

    public static bool TryToNumber(ALKScriptValue value, out double number)
    {
      switch (value)
      {
        case IntValue intValue:
          number = intValue.Value;
          return true;
        case FloatValue floatValue:
          number = floatValue.Value;
          return true;
        default:
          number = 0;
          return false;
      }
    }

    public static string Stringify(ALKScriptValue value) => value.ToString() ?? string.Empty;
  }
}
