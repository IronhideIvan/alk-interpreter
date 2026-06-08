using System.Collections.Generic;
using System.Threading.Tasks;
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
  ///
  /// <c>async</c>/<see cref="Task"/>-returning throughout — see
  /// <see cref="IEvaluationContext"/> for why: this is the plumbing that lets
  /// an <c>await</c> anywhere in an expression tree suspend evaluation
  /// mid-expression and later resume it without losing any in-flight state.
  /// <see cref="AwaitExpr"/> (handled by <see cref="EvalAwait"/>) is where that
  /// suspension becomes real: awaiting a <see cref="TaskValue"/> genuinely
  /// parks the C#-compiler-generated continuation chain on the underlying
  /// <see cref="Task"/>, which is what lets the whole evaluator suspend and
  /// later resume mid-script without losing tree-walk state.
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

    public async Task<ALKScriptValue> Eval(Expr expression, ScriptEnvironment environment)
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
          return await Eval(grouping.Expression, environment);

        case ArrayLiteralExpr arrayLiteral:
          return await EvalArrayLiteral(arrayLiteral, environment);

        case AssignmentExpr assignment:
          return await EvalAssignment(assignment, environment);

        case BinaryExpr binary:
          return await EvalBinary(binary, environment);

        case UnaryExpr unary:
          return await EvalUnary(unary, environment);

        case CallExpr call:
          return await EvalCall(call, environment);

        case GetExpr get:
          return await EvalGet(get, environment);

        case IndexExpr index:
          return await EvalIndex(index, environment);

        case NewExpr newExpr:
          return await EvalNew(newExpr, environment);

        case AwaitExpr awaitExpr:
          return await EvalAwait(awaitExpr, environment);

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

    private async Task<ALKScriptValue> EvalArrayLiteral(ArrayLiteralExpr expression, ScriptEnvironment environment)
    {
      var items = new List<ALKScriptValue>(expression.Elements.Count);

      foreach (var element in expression.Elements)
      {
        items.Add(await Eval(element, environment));

        if (_context.Signal != null)
        {
          return NullValue.Instance;
        }
      }

      return new ArrayValue(items);
    }

    private async Task<ALKScriptValue> EvalAssignment(AssignmentExpr expression, ScriptEnvironment environment)
    {
      var value = await Eval(expression.Value, environment);

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
          var target = await Eval(get.Target, environment);
          if (_context.Signal != null)
          {
            return NullValue.Instance;
          }
          var instance = target as InstanceValue
            ?? throw new RuntimeException(get.Name, $"Cannot set property '{get.Name.Lexeme}' on a value of type '{target.TypeName}'.");
          instance.Fields[get.Name.Lexeme] = value;
          return value;

        case IndexExpr index:
          var indexed = await Eval(index.Target, environment);
          if (_context.Signal != null)
          {
            return NullValue.Instance;
          }
          var array = indexed as ArrayValue
            ?? throw new RuntimeException(index.ClosingBracket, $"Cannot index into a value of type '{indexed.TypeName}'.");
          var indexValue = await Eval(index.Index, environment);
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

    private async Task<ALKScriptValue> EvalBinary(BinaryExpr expression, ScriptEnvironment environment)
    {
      // Short-circuiting logical operators evaluate their right-hand side lazily.
      if (expression.Operator.Type == ALKScriptTokenType.AmpAmp)
      {
        var left = await Eval(expression.Left, environment);
        if (_context.Signal != null)
        {
          return NullValue.Instance;
        }
        return left.IsTruthy ? await Eval(expression.Right, environment) : left;
      }

      if (expression.Operator.Type == ALKScriptTokenType.PipePipe)
      {
        var left = await Eval(expression.Left, environment);
        if (_context.Signal != null)
        {
          return NullValue.Instance;
        }
        return left.IsTruthy ? left : await Eval(expression.Right, environment);
      }

      var leftValue = await Eval(expression.Left, environment);
      if (_context.Signal != null)
      {
        return NullValue.Instance;
      }

      var rightValue = await Eval(expression.Right, environment);
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

    private async Task<ALKScriptValue> EvalUnary(UnaryExpr expression, ScriptEnvironment environment)
    {
      var operand = await Eval(expression.Operand, environment);

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

    private async Task<ALKScriptValue> EvalCall(CallExpr expression, ScriptEnvironment environment)
    {
      var callee = await Eval(expression.Callee, environment);

      if (_context.Signal != null)
      {
        return NullValue.Instance;
      }

      var arguments = new List<ALKScriptValue>(expression.Arguments.Count);
      foreach (var argument in expression.Arguments)
      {
        arguments.Add(await Eval(argument, environment));

        if (_context.Signal != null)
        {
          return NullValue.Instance;
        }
      }

      return await _context.Call(callee, arguments, expression.ClosingParen);
    }

    /// <summary>
    /// Evaluates <c>await &lt;operand&gt;</c>.
    ///
    /// Two shapes genuinely suspend here — both ultimately by `await`ing a
    /// real <see cref="System.Threading.Tasks.Task{TResult}"/>, which is what
    /// parks the whole compiler-generated continuation chain (this method,
    /// its caller, theirs, ... all the way up through <see cref="IEvaluationContext"/>)
    /// and resumes it later — possibly long after this call returns control
    /// to whatever is pumping the scheduler — exactly where it left off, with
    /// no hand-written state machine required:
    ///
    /// - <see cref="TaskValue"/> — an already-running (or already-completed)
    ///   operation: what a synchronously-resolving native, or an eager-start
    ///   `async` *function* call (mirroring C#/JS), produces.
    /// - <see cref="PendingOperationValue"/> — a not-yet-started "lazy/deferred
    ///   start" `async native` operation (see docs/ASYNC_AWAIT_DESIGN.md's core
    ///   requirements and <see cref="ALKScript.Interpreter.Common.Evaluation.Scheduling.IAsyncOperationBinder"/>):
    ///   `await` is what "Suspend"s it — its <see cref="PendingOperationValue.Start"/>
    ///   is what actually kicks off the host-side effect, at the moment (and
    ///   only if) the script genuinely needs its result, fulfilling the "must
    ///   not begin running merely because it was called" requirement.
    ///
    /// A faulted task surfaces as a catchable script-level <see cref="Signal.Thrown"/>
    /// (mirroring a "throw" of the fault's message) rather than tearing down
    /// the whole evaluation — except for <see cref="RuntimeException"/>, which
    /// signals an interpreter-level error and is left to propagate as-is.
    ///
    /// Awaiting any other kind of value is permissive identity — it simply
    /// yields the value, mirroring "awaiting an already-resolved value" in
    /// other async languages — so e.g. `await 1` is harmless rather than a
    /// type error.
    /// </summary>
    private async Task<ALKScriptValue> EvalAwait(AwaitExpr expression, ScriptEnvironment environment)
    {
      var operand = await Eval(expression.Operand, environment);

      if (_context.Signal != null)
      {
        return NullValue.Instance;
      }

      Task<ALKScriptValue> task;

      switch (operand)
      {
        case TaskValue taskValue:
          task = taskValue.Task;
          break;

        case PendingOperationValue pending:
          task = pending.Start();
          break;

        default:
          return operand;
      }

      try
      {
        return await task;
      }
      catch (RuntimeException)
      {
        throw;
      }
      catch (System.Exception exception)
      {
        _context.Signal = Signal.Thrown(new StringValue(exception.Message));
        return NullValue.Instance;
      }
    }

    private async Task<ALKScriptValue> EvalGet(GetExpr expression, ScriptEnvironment environment)
    {
      var target = await Eval(expression.Target, environment);

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

          var member = instance.Class.FindMember(expression.Name.Lexeme, out var declaringClass);
          if (member is MethodDecl method)
          {
            return _functionValueFactory.CreateMethod(method, declaringClass!, ClassEnvironments.For(instance.Class), instance);
          }

          throw new RuntimeException(expression.Name, $"Undefined property '{expression.Name.Lexeme}' on '{target.TypeName}'.");

        case ClassValue classValue:
          var staticMember = classValue.FindMember(expression.Name.Lexeme, out var staticDeclaringClass);
          if (staticMember is MethodDecl staticMethod)
          {
            return _functionValueFactory.CreateMethod(staticMethod, staticDeclaringClass!, ClassEnvironments.For(classValue), boundInstance: null);
          }

          throw new RuntimeException(expression.Name, $"Undefined static member '{expression.Name.Lexeme}' on '{target.TypeName}'.");

        default:
          throw new RuntimeException(expression.Name, $"Cannot access property '{expression.Name.Lexeme}' on a value of type '{target.TypeName}'.");
      }
    }

    private async Task<ALKScriptValue> EvalIndex(IndexExpr expression, ScriptEnvironment environment)
    {
      var target = await Eval(expression.Target, environment);

      if (_context.Signal != null)
      {
        return NullValue.Instance;
      }

      var array = target as ArrayValue
        ?? throw new RuntimeException(expression.ClosingBracket, $"Cannot index into a value of type '{target.TypeName}'.");

      var indexValue = await Eval(expression.Index, environment);

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

    private async Task<ALKScriptValue> EvalNew(NewExpr expression, ScriptEnvironment environment)
    {
      var callee = Names.LookUp(expression.TypeName, environment);
      var classValue = callee as ClassValue
        ?? throw new RuntimeException(expression.TypeName, $"'{expression.TypeName.Lexeme}' is not a class.");

      var arguments = new List<ALKScriptValue>(expression.Arguments.Count);
      foreach (var argument in expression.Arguments)
      {
        arguments.Add(await Eval(argument, environment));

        if (_context.Signal != null)
        {
          return NullValue.Instance;
        }
      }

      return await _context.Construct(classValue, arguments, expression.Keyword);
    }
  }
}
