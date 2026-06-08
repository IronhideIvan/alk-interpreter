using System.Collections.Generic;
using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Common.Evaluation;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Evaluator
{
  /// <summary>
  /// Evaluates expressions: dispatches on <see cref="Expr"/> shape, producing
  /// <see cref="ALKScriptValue"/>s. Operator semantics are delegated to
  /// <see cref="Operators"/>; calls/construction are delegated through
  /// <see cref="IEvaluationContext"/> to <see cref="CallInvoker"/>.
  /// </summary>
  internal class ExpressionEvaluator : IExpressionEvaluator
  {
    private readonly IEvaluationContext _context;
    private readonly IFunctionValueFactory _functionValueFactory;

    public ExpressionEvaluator(IEvaluationContext context, IFunctionValueFactory functionValueFactory)
    {
      _context = context;
      _functionValueFactory = functionValueFactory;
    }

    public ALKScriptValue Eval(Expr expression, ScriptEnvironment environment)
    {
      if (_context.Signal != null)
      {
        return NullValue.Instance;
      }

      switch (expression)
      {
        case LiteralExpr literal:
          return EvalLiteral(literal);

        case IdentifierExpr identifier:
          return Names.LookUp(identifier.Name, environment);

        case ThisExpr thisExpr:
          return Names.LookUp(thisExpr.Keyword, environment);

        case BaseExpr baseExpr:
          return Names.LookUp(baseExpr.Keyword, environment);

        case GroupingExpr grouping:
          return Eval(grouping.Expression, environment);

        case ArrayLiteralExpr arrayLiteral:
          return EvalArrayLiteral(arrayLiteral, environment);

        case AssignmentExpr assignment:
          return EvalAssignment(assignment, environment);

        case BinaryExpr binary:
          return EvalBinary(binary, environment);

        case UnaryExpr unary:
          return EvalUnary(unary, environment);

        case CallExpr call:
          return EvalCall(call, environment);

        case GetExpr get:
          return EvalGet(get, environment);

        case IndexExpr index:
          return EvalIndex(index, environment);

        case NewExpr newExpr:
          return EvalNew(newExpr, environment);

        case AwaitExpr awaitExpr:
          return Eval(awaitExpr.Operand, environment);

        default:
          throw new RuntimeException(
            AstTokenLocator.Of(expression),
            $"Evaluation of '{expression.GetType().Name}' is not yet supported.");
      }
    }

    private static ALKScriptValue EvalLiteral(LiteralExpr literal)
    {
      switch (literal.Value)
      {
        case null:
          return NullValue.Instance;
        case bool boolValue:
          return BoolValue.Of(boolValue);
        case long longValue:
          return new IntValue(longValue);
        case int intValue:
          return new IntValue(intValue);
        case double doubleValue:
          return new FloatValue(doubleValue);
        case string stringValue:
          return new StringValue(stringValue);
        default:
          throw new RuntimeException(literal.Token, $"Unsupported literal value '{literal.Value}'.");
      }
    }

    private ALKScriptValue EvalArrayLiteral(ArrayLiteralExpr expression, ScriptEnvironment environment)
    {
      var items = new List<ALKScriptValue>(expression.Elements.Count);

      foreach (var element in expression.Elements)
      {
        items.Add(Eval(element, environment));

        if (_context.Signal != null)
        {
          return NullValue.Instance;
        }
      }

      return new ArrayValue(items);
    }

    private ALKScriptValue EvalAssignment(AssignmentExpr expression, ScriptEnvironment environment)
    {
      var value = Eval(expression.Value, environment);

      if (_context.Signal != null)
      {
        return NullValue.Instance;
      }

      switch (expression.Target)
      {
        case IdentifierExpr identifier:
          if (!environment.TryAssign(identifier.Name.Lexeme, value))
          {
            throw new RuntimeException(identifier.Name, $"Undefined name '{identifier.Name.Lexeme}'.");
          }
          return value;

        case GetExpr get:
          var target = Eval(get.Target, environment);
          if (_context.Signal != null)
          {
            return NullValue.Instance;
          }
          var instance = target as InstanceValue
            ?? throw new RuntimeException(get.Name, $"Cannot set property '{get.Name.Lexeme}' on a value of type '{target.TypeName}'.");
          instance.Fields[get.Name.Lexeme] = value;
          return value;

        case IndexExpr index:
          var indexed = Eval(index.Target, environment);
          if (_context.Signal != null)
          {
            return NullValue.Instance;
          }
          var array = indexed as ArrayValue
            ?? throw new RuntimeException(index.ClosingBracket, $"Cannot index into a value of type '{indexed.TypeName}'.");
          var indexValue = Eval(index.Index, environment);
          if (_context.Signal != null)
          {
            return NullValue.Instance;
          }
          int position = ExpectIndex(indexValue, index.ClosingBracket, array.Items.Count);
          array.Items[position] = value;
          return value;

        default:
          throw new RuntimeException(AstTokenLocator.Of(expression.Target), "Invalid assignment target.");
      }
    }

    private ALKScriptValue EvalBinary(BinaryExpr expression, ScriptEnvironment environment)
    {
      // Short-circuiting logical operators evaluate their right-hand side lazily.
      if (expression.Operator.Type == ALKScriptTokenType.AmpAmp)
      {
        var left = Eval(expression.Left, environment);
        if (_context.Signal != null)
        {
          return NullValue.Instance;
        }
        return left.IsTruthy ? Eval(expression.Right, environment) : left;
      }

      if (expression.Operator.Type == ALKScriptTokenType.PipePipe)
      {
        var left = Eval(expression.Left, environment);
        if (_context.Signal != null)
        {
          return NullValue.Instance;
        }
        return left.IsTruthy ? left : Eval(expression.Right, environment);
      }

      var leftValue = Eval(expression.Left, environment);
      if (_context.Signal != null)
      {
        return NullValue.Instance;
      }

      var rightValue = Eval(expression.Right, environment);
      if (_context.Signal != null)
      {
        return NullValue.Instance;
      }

      var op = expression.Operator;

      switch (op.Type)
      {
        case ALKScriptTokenType.Plus:
          return Operators.Add(leftValue, rightValue, op);
        case ALKScriptTokenType.Minus:
          return Operators.Arithmetic(leftValue, rightValue, op, (a, b) => a - b, (a, b) => a - b);
        case ALKScriptTokenType.Star:
          return Operators.Arithmetic(leftValue, rightValue, op, (a, b) => a * b, (a, b) => a * b);
        case ALKScriptTokenType.Slash:
          return Operators.Arithmetic(leftValue, rightValue, op, (a, b) => a / b, (a, b) => a / b);
        case ALKScriptTokenType.Percent:
          return Operators.Arithmetic(leftValue, rightValue, op, (a, b) => a % b, (a, b) => a % b);

        case ALKScriptTokenType.Less:
          return BoolValue.Of(Operators.Compare(leftValue, rightValue, op) < 0);
        case ALKScriptTokenType.LessEqual:
          return BoolValue.Of(Operators.Compare(leftValue, rightValue, op) <= 0);
        case ALKScriptTokenType.Greater:
          return BoolValue.Of(Operators.Compare(leftValue, rightValue, op) > 0);
        case ALKScriptTokenType.GreaterEqual:
          return BoolValue.Of(Operators.Compare(leftValue, rightValue, op) >= 0);

        case ALKScriptTokenType.EqualEqual:
          return BoolValue.Of(Operators.AreEqual(leftValue, rightValue));
        case ALKScriptTokenType.BangEqual:
          return BoolValue.Of(!Operators.AreEqual(leftValue, rightValue));

        default:
          throw new RuntimeException(op, $"Unsupported binary operator '{op.Lexeme}'.");
      }
    }

    private ALKScriptValue EvalUnary(UnaryExpr expression, ScriptEnvironment environment)
    {
      var operand = Eval(expression.Operand, environment);

      if (_context.Signal != null)
      {
        return NullValue.Instance;
      }

      switch (expression.Operator.Type)
      {
        case ALKScriptTokenType.Bang:
          return BoolValue.Of(!operand.IsTruthy);

        case ALKScriptTokenType.Minus:
          switch (operand)
          {
            case IntValue intValue:
              return new IntValue(-intValue.Value);
            case FloatValue floatValue:
              return new FloatValue(-floatValue.Value);
            default:
              throw new RuntimeException(expression.Operator, $"Operator '-' cannot be applied to '{operand.TypeName}'.");
          }

        default:
          throw new RuntimeException(expression.Operator, $"Unsupported unary operator '{expression.Operator.Lexeme}'.");
      }
    }

    private ALKScriptValue EvalCall(CallExpr expression, ScriptEnvironment environment)
    {
      var callee = Eval(expression.Callee, environment);

      if (_context.Signal != null)
      {
        return NullValue.Instance;
      }

      var arguments = new List<ALKScriptValue>(expression.Arguments.Count);
      foreach (var argument in expression.Arguments)
      {
        arguments.Add(Eval(argument, environment));

        if (_context.Signal != null)
        {
          return NullValue.Instance;
        }
      }

      return _context.Call(callee, arguments, expression.ClosingParen);
    }

    private ALKScriptValue EvalGet(GetExpr expression, ScriptEnvironment environment)
    {
      var target = Eval(expression.Target, environment);

      if (_context.Signal != null)
      {
        return NullValue.Instance;
      }

      switch (target)
      {
        case InstanceValue instance:
          if (instance.Fields.TryGetValue(expression.Name.Lexeme, out var fieldValue))
          {
            return fieldValue;
          }

          var member = instance.Class.FindMember(expression.Name.Lexeme);
          if (member is MethodDecl method)
          {
            var methodValue = _functionValueFactory.Create(FunctionValueFactory.MethodAsFunctionDecl(method), ClassEnvironments.For(instance.Class));
            return methodValue is FunctionValue boundable ? boundable.BindTo(instance) : methodValue;
          }

          throw new RuntimeException(expression.Name, $"Undefined property '{expression.Name.Lexeme}' on '{target.TypeName}'.");

        case ClassValue classValue:
          var staticMember = classValue.FindMember(expression.Name.Lexeme);
          if (staticMember is MethodDecl staticMethod)
          {
            return _functionValueFactory.Create(FunctionValueFactory.MethodAsFunctionDecl(staticMethod), ClassEnvironments.For(classValue));
          }

          throw new RuntimeException(expression.Name, $"Undefined static member '{expression.Name.Lexeme}' on '{target.TypeName}'.");

        default:
          throw new RuntimeException(expression.Name, $"Cannot access property '{expression.Name.Lexeme}' on a value of type '{target.TypeName}'.");
      }
    }

    private ALKScriptValue EvalIndex(IndexExpr expression, ScriptEnvironment environment)
    {
      var target = Eval(expression.Target, environment);

      if (_context.Signal != null)
      {
        return NullValue.Instance;
      }

      var array = target as ArrayValue
        ?? throw new RuntimeException(expression.ClosingBracket, $"Cannot index into a value of type '{target.TypeName}'.");

      var indexValue = Eval(expression.Index, environment);

      if (_context.Signal != null)
      {
        return NullValue.Instance;
      }

      int position = ExpectIndex(indexValue, expression.ClosingBracket, array.Items.Count);
      return array.Items[position];
    }

    private static int ExpectIndex(ALKScriptValue value, ALKScriptToken site, int length)
    {
      if (!(value is IntValue intValue))
      {
        throw new RuntimeException(site, $"Array index must be an int, but got '{value.TypeName}'.");
      }

      long index = intValue.Value;
      if (index < 0 || index >= length)
      {
        throw new RuntimeException(site, $"Index {index} is out of bounds for an array of length {length}.");
      }

      return (int)index;
    }

    private ALKScriptValue EvalNew(NewExpr expression, ScriptEnvironment environment)
    {
      var callee = Names.LookUp(expression.TypeName, environment);
      var classValue = callee as ClassValue
        ?? throw new RuntimeException(expression.TypeName, $"'{expression.TypeName.Lexeme}' is not a class.");

      var arguments = new List<ALKScriptValue>(expression.Arguments.Count);
      foreach (var argument in expression.Arguments)
      {
        arguments.Add(Eval(argument, environment));

        if (_context.Signal != null)
        {
          return NullValue.Instance;
        }
      }

      return _context.Construct(classValue, arguments, expression.Keyword);
    }
  }
}
