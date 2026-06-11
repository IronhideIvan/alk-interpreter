using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Common.Evaluation;
using ALKScript.Interpreter.Common.Evaluation.Scheduling;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Common.Token;
using ALKScript.Interpreter.Evaluator.Scheduling;

namespace ALKScript.Interpreter.Evaluator
{
  /// <summary>
  /// Evaluates expressions: dispatches on <see cref="Expr"/> shape, producing
  /// <see cref="ALKScriptValue"/>s. Operator semantics are delegated to
  /// <see cref="Operators"/>; calls/construction are delegated through
  /// <see cref="IEvaluationContext"/> to <see cref="CallInvoker"/>.
  ///
  /// <c>async</c>/<see cref="Task"/>-returning throughout, so an <c>await</c>
  /// anywhere in an expression tree can suspend evaluation mid-expression and
  /// resume later without losing in-flight state — see
  /// <see cref="IEvaluationContext"/>. <see cref="EvalAwait"/> is where that
  /// suspension becomes real: awaiting a <see cref="ThunkValue"/> parks the
  /// compiler-generated continuation chain on the underlying <see cref="Task"/>.
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

        case PrefixUpdateExpr prefixUpdate:
          return (await EvalUpdate(prefixUpdate.Operand, prefixUpdate.Operator, environment)).NewValue;

        case PostfixUpdateExpr postfixUpdate:
          return (await EvalUpdate(postfixUpdate.Operand, postfixUpdate.Operator, environment)).OldValue;

        case CompoundAssignmentExpr compound:
          return await EvalCompoundAssignment(compound, environment);

        case TernaryExpr ternary:
          return await EvalTernary(ternary, environment);

        case NullConditionalGetExpr nullCondGet:
          return await EvalNullConditionalGet(nullCondGet, environment);

        case InterpolatedStringExpr interpolated:
          return await EvalInterpolatedString(interpolated, environment);

        case TypeTestExpr typeTest:
          return await EvalTypeTest(typeTest, environment);

        case TypeCastExpr typeCast:
          return await EvalTypeCast(typeCast, environment);

        case CastExpr cast:
          return await EvalCast(cast, environment);

        case LambdaExpr lambda:
          return EvalLambda(lambda, environment);

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

    private async Task<ALKScriptValue> EvalInterpolatedString(InterpolatedStringExpr expression, ScriptEnvironment environment)
    {
      var builder = new System.Text.StringBuilder();

      builder.Append(expression.Parts[0]);

      for (int i = 0; i < expression.Expressions.Count; i++)
      {
        var value = await Eval(expression.Expressions[i], environment);

        if (_context.Signal != null)
        {
          return NullValue.Instance;
        }

        builder.Append(Operators.Stringify(value));
        builder.Append(expression.Parts[i + 1]);
      }

      return new StringValue(builder.ToString());
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
          if (environment.IsConst(identifier.Name.Lexeme))
          {
            throw new RuntimeException(identifier.Name, $"Cannot assign to 'const' variable '{identifier.Name.Lexeme}'.");
          }
          environment.TryGetDeclaredType(identifier.Name.Lexeme, out var declaredType);
          TypeChecking.EnsureAssignable(declaredType, value, identifier.Name, $"variable '{identifier.Name.Lexeme}'", environment);
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

          if (target is ClassValue staticTargetClass)
          {
            var (staticDeclaringClass, staticField) = ResolveStaticField(staticTargetClass, get.Name, environment);
            TypeChecking.EnsureAssignable(staticField.Type, value, get.Name, $"static field '{get.Name.Lexeme}'", environment);
            staticDeclaringClass.StaticFields[get.Name.Lexeme] = value;
            return value;
          }

          var instance = target as InstanceValue
            ?? throw new RuntimeException(get.Name, $"Cannot set property '{get.Name.Lexeme}' on a value of type '{target.TypeName}'.");
          var fieldMemberForWrite = instance.Class.FindMember(get.Name.Lexeme, out var fieldWriteDeclaringClass);
          if (fieldMemberForWrite != null)
          {
            EnforceAccessModifier(fieldMemberForWrite, fieldWriteDeclaringClass, get.Name, environment);
            EnforceFieldWritable(fieldMemberForWrite, fieldWriteDeclaringClass, get.Name, environment);

            if (fieldMemberForWrite is FieldDecl fieldDecl)
            {
              TypeChecking.EnsureAssignable(fieldDecl.Type, value, get.Name, $"field '{get.Name.Lexeme}'", environment, instance.TypeArguments);
            }
          }
          instance.Fields[get.Name.Lexeme] = value;
          return value;

        case IndexExpr index:
          var indexed = await Eval(index.Target, environment);
          if (_context.Signal != null)
          {
            return NullValue.Instance;
          }
          if (indexed is StringValue)
          {
            throw new RuntimeException(index.ClosingBracket, "Cannot assign to a string index; strings are immutable.");
          }

          var array = indexed as ArrayValue
            ?? throw new RuntimeException(index.ClosingBracket, $"Cannot index into a value of type '{indexed.TypeName}'.");
          var indexValue = await Eval(index.Index, environment);
          if (_context.Signal != null)
          {
            return NullValue.Instance;
          }
          int position = ExpectIndex(indexValue, index.ClosingBracket, array.Items.Count, "Array");
          array.Items[position] = value;
          return value;

        default:
          throw new RuntimeException(AstTokenLocator.Of(expression.Target), "Invalid assignment target.");
      }
    }

    /// <summary>
    /// Reads the current value of <paramref name="operand"/>, steps it by ±1,
    /// writes the new value back in place, and returns both values so callers
    /// can choose the pre-update (postfix) or post-update (prefix) result.
    /// Sub-expressions inside <see cref="GetExpr"/> and <see cref="IndexExpr"/>
    /// targets are evaluated exactly once, avoiding double side-effects.
    /// </summary>
    private async Task<(ALKScriptValue OldValue, ALKScriptValue NewValue)> EvalUpdate(
      Expr operand, ALKScriptToken op, ScriptEnvironment environment)
    {
      switch (operand)
      {
        case IdentifierExpr identifier:
        {
          if (environment.IsConst(identifier.Name.Lexeme))
          {
            throw new RuntimeException(identifier.Name, $"Cannot assign to 'const' variable '{identifier.Name.Lexeme}'.");
          }
          var old = Names.LookUp(identifier.Name, environment);
          var next = Step(old, op);
          if (!environment.TryAssign(identifier.Name.Lexeme, next))
            throw new RuntimeException(identifier.Name, $"Undefined name '{identifier.Name.Lexeme}'.");
          return (old, next);
        }

        case GetExpr get:
        {
          var target = await Eval(get.Target, environment);
          if (_context.Signal != null)
            return (NullValue.Instance, NullValue.Instance);

          if (target is ClassValue staticTargetClass)
          {
            var (staticDeclaringClass, _) = ResolveStaticField(staticTargetClass, get.Name, environment);
            var staticOld = staticDeclaringClass.StaticFields[get.Name.Lexeme];
            var staticNext = Step(staticOld, op);
            staticDeclaringClass.StaticFields[get.Name.Lexeme] = staticNext;
            return (staticOld, staticNext);
          }

          var instance = target as InstanceValue
            ?? throw new RuntimeException(get.Name, $"Cannot apply '{op.Lexeme}' to a value of type '{target.TypeName}'.");
          if (!instance.Fields.TryGetValue(get.Name.Lexeme, out var old))
            throw new RuntimeException(get.Name, $"Undefined field '{get.Name.Lexeme}'.");
          var fieldMemberForUpdate = instance.Class.FindMember(get.Name.Lexeme, out var fieldUpdateDeclaringClass);
          EnforceFieldWritable(fieldMemberForUpdate, fieldUpdateDeclaringClass, get.Name, environment);
          var next = Step(old, op);
          instance.Fields[get.Name.Lexeme] = next;
          return (old, next);
        }

        case IndexExpr index:
        {
          var target = await Eval(index.Target, environment);
          if (_context.Signal != null)
            return (NullValue.Instance, NullValue.Instance);
          if (target is StringValue)
          {
            throw new RuntimeException(index.ClosingBracket, "Cannot assign to a string index; strings are immutable.");
          }
          var array = target as ArrayValue
            ?? throw new RuntimeException(index.ClosingBracket, $"Cannot index into a value of type '{target.TypeName}'.");
          var indexValue = await Eval(index.Index, environment);
          if (_context.Signal != null)
            return (NullValue.Instance, NullValue.Instance);
          int position = ExpectIndex(indexValue, index.ClosingBracket, array.Items.Count, "Array");
          var old = array.Items[position];
          var next = Step(old, op);
          array.Items[position] = next;
          return (old, next);
        }

        default:
          throw new RuntimeException(op, $"'{op.Lexeme}' requires a variable, field, or array element.");
      }
    }

    private static ALKScriptValue Step(ALKScriptValue value, ALKScriptToken op)
    {
      bool increment = op.Type == ALKScriptTokenType.PlusPlus;
      switch (value)
      {
        case IntValue i:   return new IntValue(i.Value + (increment ? 1L : -1L));
        case FloatValue f: return new FloatValue(f.Value + (increment ? 1.0 : -1.0));
        default:
          throw new RuntimeException(op, $"'{op.Lexeme}' cannot be applied to a value of type '{value.TypeName}'.");
      }
    }

    private async Task<ALKScriptValue> EvalBinary(BinaryExpr expression, ScriptEnvironment environment)
    {
      // Null-coalescing: left ?? right — return left if non-null, else right.
      if (expression.Operator.Type == ALKScriptTokenType.QuestionQuestion)
      {
        var left = await Eval(expression.Left, environment);
        if (_context.Signal != null)
        {
          return NullValue.Instance;
        }
        if (!(left is NullValue))
        {
          return left;
        }
        return await Eval(expression.Right, environment);
      }

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

        case ALKScriptTokenType.Amp:
          return Operators.Bitwise(leftValue, rightValue, op, (a, b) => a & b);
        case ALKScriptTokenType.Pipe:
          return Operators.Bitwise(leftValue, rightValue, op, (a, b) => a | b);
        case ALKScriptTokenType.Caret:
          return Operators.Bitwise(leftValue, rightValue, op, (a, b) => a ^ b);
        case ALKScriptTokenType.LessLess:
          return Operators.Bitwise(leftValue, rightValue, op, (a, b) => a << (int)b);
        case ALKScriptTokenType.GreaterGreater:
          return Operators.Bitwise(leftValue, rightValue, op, (a, b) => a >> (int)b);

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

        case ALKScriptTokenType.Tilde:
          switch (operand)
          {
            case IntValue intValue:
              return new IntValue(~intValue.Value);
            default:
              throw new RuntimeException(expression.Operator, $"Operator '~' cannot be applied to '{operand.TypeName}'; bitwise operators require 'int' operands.");
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

      // null-conditional short-circuit: obj?.method(...) → null when obj is null
      if (callee is NullValue && expression.Callee is NullConditionalGetExpr)
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
    /// Two shapes genuinely suspend, by awaiting a real <see cref="Task{TResult}"/>
    /// that parks the whole continuation chain and resumes it later exactly
    /// where it left off:
    ///
    /// - <see cref="ThunkValue"/> — an already-running/completed operation, as
    ///   produced by a synchronous native returning <c>thunk</c>/<c>thunk&lt;T&gt;</c>.
    /// - <see cref="PendingOperationValue"/> — a not-yet-started `async native`
    ///   operation (docs/ASYNC_AWAIT_DESIGN.md core requirements,
    ///   <see cref="ALKScript.Interpreter.Common.Evaluation.Scheduling.IAsyncOperationBinder"/>):
    ///   `await` triggers its <see cref="PendingOperationValue.Start"/>, so the
    ///   host effect only begins once the script actually needs the result.
    ///
    /// A faulted task surfaces as a catchable <see cref="Signal.Thrown"/>
    /// (like a script-level "throw") rather than tearing down the whole
    /// evaluation — except <see cref="RuntimeException"/>, which propagates as-is.
    ///
    /// Awaiting any other value is identity — `await 1` simply yields `1`.
    /// </summary>
    private async Task<ALKScriptValue> EvalAwait(AwaitExpr expression, ScriptEnvironment environment)
    {
      var operand = await Eval(expression.Operand, environment);

      if (_context.Signal != null)
      {
        return NullValue.Instance;
      }

      switch (operand)
      {
        case ArrayValue array:
          return await EvalWhenAll(array.Items);

        default:
          return await AwaitIfNeeded(operand);
      }
    }

    /// <summary>
    /// Resolves <paramref name="value"/> to its settled result if it is a
    /// <see cref="ThunkValue"/> or <see cref="PendingOperationValue"/> (the
    /// shapes a <c>thunk</c>/<c>thunk&lt;T&gt;</c>-typed expression evaluates
    /// to), otherwise returns it unchanged.
    /// </summary>
    private async Task<ALKScriptValue> AwaitIfNeeded(ALKScriptValue value)
    {
      switch (value)
      {
        case ThunkValue thunkValue:
          return await AwaitTask(thunkValue.Task);

        case PendingOperationValue pending:
          return await AwaitPending(pending);

        default:
          return value;
      }
    }

    private async Task<ALKScriptValue> AwaitTask(Task<ALKScriptValue> task)
    {
      try
      {
        return await task.ScheduledOn(_context.Scheduler);
      }
      catch (RuntimeException)
      {
        throw;
      }
      catch (Exception exception)
      {
        _context.Signal = Signal.Thrown(new StringValue(exception.Message));
        return NullValue.Instance;
      }
    }

    /// <summary>
    /// Handles <c>await &lt;pendingOperationValue&gt;</c> with record-and-replay
    /// awareness. During replay (log not yet exhausted), the next log entry is
    /// consumed positionally and its recorded result or fault is returned
    /// immediately — the host-side effect is not started. During live execution,
    /// <see cref="PendingOperationValue.Start"/> is called, the result is
    /// awaited, and an <see cref="OperationLogEntry"/> is appended to the log
    /// for future replay.
    /// </summary>
    private async Task<ALKScriptValue> AwaitPending(PendingOperationValue pending)
    {
      var entry = _context.TryReplayNext();
      if (entry != null)
      {
        pending.MarkReplayed();
        if (entry.IsFaulted)
        {
          _context.Signal = Signal.Thrown(new StringValue(entry.FaultMessage!));
          return NullValue.Instance;
        }
        return entry.Result!;
      }

      try
      {
        var result = await pending.Start().ScheduledOn(_context.Scheduler);
        _context.RecordEntry(OperationLogEntry.FromResult(pending.Operation, result));
        return result;
      }
      catch (RuntimeException)
      {
        throw;
      }
      catch (Exception exception)
      {
        _context.RecordEntry(OperationLogEntry.FromFault(pending.Operation, exception.Message));
        _context.Signal = Signal.Thrown(new StringValue(exception.Message));
        return NullValue.Instance;
      }
    }

    /// <summary>
    /// Implements <c>await [a, b, …]</c> — sugar for <c>Task.whenAll</c> (see
    /// docs/ASYNC_AWAIT_DESIGN.md decision #13). Record-and-replay aware: each
    /// <see cref="PendingOperationValue"/> element is tried against the replay
    /// log first (consuming the next entry positionally); elements not covered
    /// by the log are started live. <see cref="Task.WhenAll"/> provides the
    /// run-to-completion guarantee for the live portion. Live results and faults
    /// are recorded to the log in source (array-element) order so replays
    /// encounter them in the same sequence. Individual faults are also reported
    /// to the host (decision #11) and aggregated into one catchable
    /// <see cref="Signal.Thrown"/>.
    /// </summary>
    private async Task<ALKScriptValue> EvalWhenAll(IReadOnlyList<ALKScriptValue> items)
    {
      var count = items.Count;
      var resolved = new ALKScriptValue?[count];
      var faultMessages = new string?[count];
      var pendingOps = new PendingOperationValue?[count];
      var liveTasks = new Task<ALKScriptValue>?[count];
      var liveCount = 0;

      // First pass: replay what the log covers; queue up the rest as live tasks.
      for (int i = 0; i < count; i++)
      {
        switch (items[i])
        {
          case PendingOperationValue pov:
            pendingOps[i] = pov;
            var entry = _context.TryReplayNext();
            if (entry != null)
            {
              pov.MarkReplayed();
              if (entry.IsFaulted) faultMessages[i] = entry.FaultMessage;
              else resolved[i] = entry.Result;
            }
            else
            {
              liveTasks[i] = pov.Start();
              liveCount++;
            }
            break;

          case ThunkValue tv:
            liveTasks[i] = tv.Task;
            liveCount++;
            break;

          default:
            resolved[i] = items[i];
            break;
        }
      }

      // Await all live tasks concurrently.
      if (liveCount > 0)
      {
        var taskList = new List<Task<ALKScriptValue>>(liveCount);
        var indices = new List<int>(liveCount);
        for (int i = 0; i < count; i++)
        {
          if (liveTasks[i] != null) { taskList.Add(liveTasks[i]!); indices.Add(i); }
        }

        try
        {
          var liveResults = await Task.WhenAll(taskList).ScheduledOn(_context.Scheduler);
          for (int j = 0; j < indices.Count; j++)
          {
            int i = indices[j];
            resolved[i] = liveResults[j];
            if (pendingOps[i] != null)
              _context.RecordEntry(OperationLogEntry.FromResult(pendingOps[i]!.Operation, resolved[i]!));
          }
        }
        catch (RuntimeException)
        {
          throw;
        }
        catch
        {
          // Task.WhenAll waited for all to settle. Collect results and faults
          // in source order, recording each to the log.
          for (int j = 0; j < indices.Count; j++)
          {
            int i = indices[j];
            if (taskList[j].IsFaulted)
            {
              var fault = taskList[j].Exception!.InnerException ?? taskList[j].Exception!;
              faultMessages[i] = fault.Message;
              if (pendingOps[i] != null)
              {
                _context.RecordEntry(OperationLogEntry.FromFault(pendingOps[i]!.Operation, fault.Message));
                _functionValueFactory.ReportOperationFaulted(pendingOps[i]!.Operation, fault);
              }
            }
            else
            {
              resolved[i] = taskList[j].Result;
              if (pendingOps[i] != null)
                _context.RecordEntry(OperationLogEntry.FromResult(pendingOps[i]!.Operation, resolved[i]!));
            }
          }
        }
      }

      // Surface any faults (from replay or live) as an aggregate thrown signal.
      var allFaults = new List<string>();
      for (int i = 0; i < count; i++)
      {
        if (faultMessages[i] != null) allFaults.Add(faultMessages[i]!);
      }

      if (allFaults.Count > 0)
      {
        var message = allFaults.Count == 1
          ? allFaults[0]
          : $"Multiple operations failed: {string.Join("; ", allFaults)}";
        _context.Signal = Signal.Thrown(new StringValue(message));
        return NullValue.Instance;
      }

      var results = new List<ALKScriptValue>(count);
      for (int i = 0; i < count; i++) results.Add(resolved[i] ?? NullValue.Instance);
      return new ArrayValue(results);
    }

    private async Task<ALKScriptValue> EvalGet(GetExpr expression, ScriptEnvironment environment)
    {
      var target = await Eval(expression.Target, environment);

      if (_context.Signal != null)
      {
        return NullValue.Instance;
      }

      return GetMember(target, expression.Name, environment);
    }

    private async Task<ALKScriptValue> EvalNullConditionalGet(NullConditionalGetExpr expression, ScriptEnvironment environment)
    {
      var target = await Eval(expression.Target, environment);

      if (_context.Signal != null)
      {
        return NullValue.Instance;
      }

      // Short-circuit on null — the whole expression becomes null.
      if (target is NullValue)
      {
        return NullValue.Instance;
      }

      return GetMember(target, expression.Name, environment);
    }

    private ALKScriptValue GetMember(ALKScriptValue target, ALKScriptToken name, ScriptEnvironment environment)
    {
      switch (target)
      {
        case InstanceValue instance:
          if (instance.Fields.TryGetValue(name.Lexeme, out var fieldValue))
          {
            // Enforce access modifier on field reads.
            var fieldMember = instance.Class.FindMember(name.Lexeme, out var fieldDeclaringClass);
            if (fieldMember != null)
            {
              EnforceAccessModifier(fieldMember, fieldDeclaringClass, name, environment);
            }
            return fieldValue;
          }

          var member = instance.Class.FindMember(name.Lexeme, out var declaringClass);
          if (member is MethodDecl method && !method.IsStatic)
          {
            EnforceAccessModifier(member, declaringClass, name, environment);
            return _functionValueFactory.CreateMethod(method, declaringClass!, ClassEnvironments.For(instance.Class), instance);
          }

          if (member is FieldDecl staticField && staticField.IsStatic)
          {
            throw new RuntimeException(name, $"Cannot access static member '{name.Lexeme}' through an instance; use '{instance.Class.Declaration.Name.Lexeme}.{name.Lexeme}'.");
          }

          if (member is MethodDecl staticMethodViaInstance && staticMethodViaInstance.IsStatic)
          {
            throw new RuntimeException(name, $"Cannot access static member '{name.Lexeme}' through an instance; use '{instance.Class.Declaration.Name.Lexeme}.{name.Lexeme}'.");
          }

          throw new RuntimeException(name, $"Undefined property '{name.Lexeme}' on '{target.TypeName}'.");

        case BaseValue baseValue:
          var baseMember = baseValue.Superclass.FindMember(name.Lexeme, out var baseDeclaringClass);
          if (baseMember is MethodDecl baseMethod)
          {
            return _functionValueFactory.CreateMethod(baseMethod, baseDeclaringClass!, ClassEnvironments.For(baseValue.Superclass), baseValue.Instance);
          }

          throw new RuntimeException(name, $"Undefined member '{name.Lexeme}' on base class.");

        case ClassValue classValue:
          var staticMember = classValue.FindMember(name.Lexeme, out var staticDeclaringClass);

          if (staticMember is MethodDecl staticMethod && staticMethod.IsStatic)
          {
            EnforceAccessModifier(staticMember, staticDeclaringClass, name, environment);
            return _functionValueFactory.CreateMethod(staticMethod, staticDeclaringClass!, ClassEnvironments.For(classValue), boundInstance: null);
          }

          if (staticMember is FieldDecl staticFieldDecl && staticFieldDecl.IsStatic)
          {
            EnforceAccessModifier(staticMember, staticDeclaringClass, name, environment);
            return staticDeclaringClass!.StaticFields[name.Lexeme];
          }

          throw new RuntimeException(name, $"Undefined static member '{name.Lexeme}' on '{target.TypeName}'.");

        case ArrayValue array:
          return GetArrayMember(array, name);

        case StringValue stringValue:
          return GetStringMember(stringValue, name);

        case NamespaceValue namespaceValue:
          if (namespaceValue.Members.TryGetValue(name.Lexeme, out var namespaceMember))
          {
            return namespaceMember;
          }

          throw new RuntimeException(name, $"Namespace '{namespaceValue.Name}' does not export '{name.Lexeme}'.");

        case EnumTypeValue enumType:
          if (enumType.Members.TryGetValue(name.Lexeme, out var enumMember))
          {
            return enumMember;
          }

          throw new RuntimeException(name, $"Enum '{enumType.Declaration.Name.Lexeme}' does not have a member '{name.Lexeme}'.");

        default:
          throw new RuntimeException(name, $"Cannot access property '{name.Lexeme}' on a value of type '{target.TypeName}'.");
      }
    }

    /// <summary>
    /// Enforces that a <c>readonly</c> field is only assigned from within the
    /// constructor of its declaring class. No-op for fields that aren't
    /// declared <c>readonly</c> (or aren't fields at all).
    /// </summary>
    private static void EnforceFieldWritable(MemberDecl? member, ClassValue? declaringClass, ALKScriptToken site, ScriptEnvironment environment)
    {
      if (member is not FieldDecl { IsReadonly: true }) return;

      if (!(environment.IsInConstructor && environment.CurrentClass == declaringClass))
      {
        throw new RuntimeException(site, $"Cannot assign to readonly field '{site.Lexeme}' outside of '{declaringClass!.Declaration.Name.Lexeme}'s constructor.");
      }
    }

    /// <summary>
    /// Enforces the access modifier of <paramref name="member"/> relative to the
    /// currently executing class (<see cref="ScriptEnvironment.CurrentClass"/>).
    /// Public members are always accessible; private members require the current
    /// class to be exactly the declaring class; protected members require it to be
    /// the declaring class or a subclass of it.
    /// </summary>
    private static void EnforceAccessModifier(
      MemberDecl member, ClassValue? declaringClass, ALKScriptToken site, ScriptEnvironment environment)
    {
      if (member.AccessModifier == AccessModifier.Public) return;

      var currentClass = environment.CurrentClass;

      if (member.AccessModifier == AccessModifier.Private)
      {
        if (currentClass == null || currentClass != declaringClass)
        {
          throw new RuntimeException(site,
            $"Cannot access private member '{site.Lexeme}' outside its declaring class.");
        }
      }
      else if (member.AccessModifier == AccessModifier.Protected)
      {
        if (!IsSubclassOrSelf(currentClass, declaringClass!))
        {
          throw new RuntimeException(site,
            $"Cannot access protected member '{site.Lexeme}' outside its declaring class or subclasses.");
        }
      }
    }

    private static bool IsSubclassOrSelf(ClassValue? candidate, ClassValue target)
    {
      for (ClassValue? c = candidate; c != null; c = c.Superclass)
      {
        if (c == target) return true;
      }
      return false;
    }

    /// <summary>
    /// Resolves <paramref name="name"/> as a "static" field on
    /// <paramref name="classValue"/> (or an inherited one), enforcing its
    /// access modifier, for "ClassName.field" write/update targets. Throws if
    /// no such static field exists.
    /// </summary>
    private static (ClassValue DeclaringClass, FieldDecl Field) ResolveStaticField(
      ClassValue classValue, ALKScriptToken name, ScriptEnvironment environment)
    {
      var member = classValue.FindMember(name.Lexeme, out var declaringClass);

      if (!(member is FieldDecl field) || !field.IsStatic)
      {
        throw new RuntimeException(name, $"Undefined static field '{name.Lexeme}' on '{classValue.Declaration.Name.Lexeme}'.");
      }

      EnforceAccessModifier(field, declaringClass, name, environment);

      return (declaringClass!, field);
    }

    private async Task<ALKScriptValue> EvalCompoundAssignment(CompoundAssignmentExpr expression, ScriptEnvironment environment)
    {
      switch (expression.Target)
      {
        case IdentifierExpr identifier:
        {
          if (environment.IsConst(identifier.Name.Lexeme))
          {
            throw new RuntimeException(identifier.Name, $"Cannot assign to 'const' variable '{identifier.Name.Lexeme}'.");
          }
          var current = Names.LookUp(identifier.Name, environment);
          var rhs = await Eval(expression.Value, environment);
          if (_context.Signal != null) return NullValue.Instance;
          var result = ApplyCompound(current, rhs, expression.Operator);
          if (!environment.TryAssign(identifier.Name.Lexeme, result))
            throw new RuntimeException(identifier.Name, $"Undefined name '{identifier.Name.Lexeme}'.");
          return result;
        }

        case GetExpr get:
        {
          var target = await Eval(get.Target, environment);
          if (_context.Signal != null) return NullValue.Instance;

          if (target is ClassValue staticTargetClass)
          {
            var (staticDeclaringClass, _) = ResolveStaticField(staticTargetClass, get.Name, environment);
            var staticCurrent = staticDeclaringClass.StaticFields[get.Name.Lexeme];
            var staticRhs = await Eval(expression.Value, environment);
            if (_context.Signal != null) return NullValue.Instance;
            var staticResult = ApplyCompound(staticCurrent, staticRhs, expression.Operator);
            staticDeclaringClass.StaticFields[get.Name.Lexeme] = staticResult;
            return staticResult;
          }

          var instance = target as InstanceValue
            ?? throw new RuntimeException(get.Name, $"Cannot apply '{expression.Operator.Lexeme}' to a value of type '{target.TypeName}'.");
          if (!instance.Fields.TryGetValue(get.Name.Lexeme, out var current))
            throw new RuntimeException(get.Name, $"Undefined field '{get.Name.Lexeme}'.");
          var fieldMemberForCompound = instance.Class.FindMember(get.Name.Lexeme, out var fieldCompoundDeclaringClass);
          EnforceFieldWritable(fieldMemberForCompound, fieldCompoundDeclaringClass, get.Name, environment);
          var rhs = await Eval(expression.Value, environment);
          if (_context.Signal != null) return NullValue.Instance;
          var result = ApplyCompound(current, rhs, expression.Operator);
          instance.Fields[get.Name.Lexeme] = result;
          return result;
        }

        case IndexExpr index:
        {
          var target = await Eval(index.Target, environment);
          if (_context.Signal != null) return NullValue.Instance;
          if (target is StringValue)
          {
            throw new RuntimeException(index.ClosingBracket, "Cannot assign to a string index; strings are immutable.");
          }
          var array = target as ArrayValue
            ?? throw new RuntimeException(index.ClosingBracket, $"Cannot index into a value of type '{target.TypeName}'.");
          var indexValue = await Eval(index.Index, environment);
          if (_context.Signal != null) return NullValue.Instance;
          int position = ExpectIndex(indexValue, index.ClosingBracket, array.Items.Count, "Array");
          var current = array.Items[position];
          var rhs = await Eval(expression.Value, environment);
          if (_context.Signal != null) return NullValue.Instance;
          var result = ApplyCompound(current, rhs, expression.Operator);
          array.Items[position] = result;
          return result;
        }

        default:
          throw new RuntimeException(expression.Operator, "Invalid assignment target for compound assignment.");
      }
    }

    private static ALKScriptValue ApplyCompound(ALKScriptValue left, ALKScriptValue right, ALKScriptToken op)
    {
      switch (op.Type)
      {
        case ALKScriptTokenType.PlusEqual:
          return Operators.Add(left, right, op);
        case ALKScriptTokenType.MinusEqual:
          return Operators.Arithmetic(left, right, op, (a, b) => a - b, (a, b) => a - b);
        case ALKScriptTokenType.StarEqual:
          return Operators.Arithmetic(left, right, op, (a, b) => a * b, (a, b) => a * b);
        case ALKScriptTokenType.SlashEqual:
          return Operators.Arithmetic(left, right, op, (a, b) => a / b, (a, b) => a / b);
        case ALKScriptTokenType.PercentEqual:
          return Operators.Arithmetic(left, right, op, (a, b) => a % b, (a, b) => a % b);
        case ALKScriptTokenType.AmpEqual:
          return Operators.Bitwise(left, right, op, (a, b) => a & b);
        case ALKScriptTokenType.PipeEqual:
          return Operators.Bitwise(left, right, op, (a, b) => a | b);
        case ALKScriptTokenType.CaretEqual:
          return Operators.Bitwise(left, right, op, (a, b) => a ^ b);
        case ALKScriptTokenType.LessLessEqual:
          return Operators.Bitwise(left, right, op, (a, b) => a << (int)b);
        case ALKScriptTokenType.GreaterGreaterEqual:
          return Operators.Bitwise(left, right, op, (a, b) => a >> (int)b);
        default:
          throw new RuntimeException(op, $"Unsupported compound assignment operator '{op.Lexeme}'.");
      }
    }

    private async Task<ALKScriptValue> EvalTernary(TernaryExpr expression, ScriptEnvironment environment)
    {
      var condition = await Eval(expression.Condition, environment);

      if (_context.Signal != null)
      {
        return NullValue.Instance;
      }

      return condition.IsTruthy
        ? await Eval(expression.ThenExpr, environment)
        : await Eval(expression.ElseExpr, environment);
    }

    private async Task<ALKScriptValue> EvalTypeTest(TypeTestExpr expression, ScriptEnvironment environment)
    {
      var operand = await Eval(expression.Operand, environment);

      if (_context.Signal != null)
      {
        return NullValue.Instance;
      }

      return BoolValue.Of(TypeChecking.MatchesType(operand, expression.Type, environment, expression.Keyword));
    }

    private async Task<ALKScriptValue> EvalTypeCast(TypeCastExpr expression, ScriptEnvironment environment)
    {
      var operand = await Eval(expression.Operand, environment);

      if (_context.Signal != null)
      {
        return NullValue.Instance;
      }

      return TypeChecking.MatchesType(operand, expression.Type, environment, expression.Keyword) ? operand : NullValue.Instance;
    }

    private async Task<ALKScriptValue> EvalCast(CastExpr expression, ScriptEnvironment environment)
    {
      var operand = await Eval(expression.Operand, environment);

      if (_context.Signal != null)
      {
        return NullValue.Instance;
      }

      switch (expression.TargetType)
      {
        case "int":
        case "long":
          switch (operand)
          {
            case IntValue intValue:
              return intValue;
            case FloatValue floatValue:
              return new IntValue((long)floatValue.Value);
            default:
              throw new RuntimeException(expression.Keyword, $"Cannot cast a value of type '{operand.TypeName}' to '{expression.TargetType}'.");
          }

        case "float":
          switch (operand)
          {
            case FloatValue floatValue:
              return floatValue;
            case IntValue intValue:
              return new FloatValue(intValue.Value);
            default:
              throw new RuntimeException(expression.Keyword, $"Cannot cast a value of type '{operand.TypeName}' to 'float'.");
          }

        default:
          throw new RuntimeException(expression.Keyword, $"Cannot cast to '{expression.TargetType}'.");
      }
    }

    /// <summary>
    /// Evaluates a lambda expression into a <see cref="FunctionValue"/> closing
    /// over <paramref name="environment"/>. When written inside a method body,
    /// the lambda captures the enclosing "this"/"base" by copying the current
    /// "this" binding and <see cref="ScriptEnvironment.CurrentClass"/> into the
    /// resulting <see cref="FunctionValue"/>, exactly like a bound method.
    /// </summary>
    private ALKScriptValue EvalLambda(LambdaExpr lambda, ScriptEnvironment environment)
    {
      var declaration = new FunctionDecl(
        isNative: false,
        typeParameters: System.Array.Empty<string>(),
        returnType: lambda.ReturnType,
        name: new ALKScriptToken(ALKScriptTokenType.Identifier, "<lambda>", lambda.Arrow.Line, lambda.Arrow.Column),
        parameters: lambda.Parameters,
        body: lambda.Body);

      InstanceValue? boundInstance = environment.TryGet("this", out var thisValue) ? thisValue as InstanceValue : null;

      return new FunctionValue(declaration, environment, boundInstance, environment.CurrentClass);
    }

    private async Task<ALKScriptValue> EvalIndex(IndexExpr expression, ScriptEnvironment environment)
    {
      var target = await Eval(expression.Target, environment);

      if (_context.Signal != null)
      {
        return NullValue.Instance;
      }

      var indexValue = await Eval(expression.Index, environment);

      if (_context.Signal != null)
      {
        return NullValue.Instance;
      }

      switch (target)
      {
        case ArrayValue array:
          int position = ExpectIndex(indexValue, expression.ClosingBracket, array.Items.Count, "Array");
          return array.Items[position];

        case StringValue stringValue:
          int charPosition = ExpectIndex(indexValue, expression.ClosingBracket, stringValue.Value.Length, "String");
          return new StringValue(stringValue.Value[charPosition].ToString());

        default:
          throw new RuntimeException(expression.ClosingBracket, $"Cannot index into a value of type '{target.TypeName}'.");
      }
    }

    private static int ExpectIndex(ALKScriptValue value, ALKScriptToken site, int length, string kind)
    {
      if (!(value is IntValue intValue))
      {
        throw new RuntimeException(site, $"{kind} index must be an int, but got '{value.TypeName}'.");
      }

      long index = intValue.Value;
      if (index < 0 || index >= length)
      {
        throw new RuntimeException(site, $"Index {index} is out of bounds for {(kind == "String" ? "a string" : "an array")} of length {length}.");
      }

      return (int)index;
    }

    /// <summary>
    /// Resolves built-in array members: the <c>length</c> property and the
    /// <c>push</c>/<c>pop</c>/<c>join</c>/<c>slice</c>/<c>remove</c>/<c>map</c>/
    /// <c>filter</c> native methods. <c>push</c>, <c>pop</c>, and <c>remove</c>
    /// mutate <paramref name="array"/> in place; <c>join</c>, <c>slice</c>,
    /// <c>map</c>, and <c>filter</c> return new arrays without modifying the
    /// receiver.
    /// </summary>
    private ALKScriptValue GetArrayMember(ArrayValue array, ALKScriptToken name)
    {
      switch (name.Lexeme)
      {
        case "length":
          return new IntValue(array.Items.Count);

        case "push":
          return new NativeFunctionValue("push", 1, arguments =>
          {
            array.Items.Add(arguments[0]);
            return new IntValue(array.Items.Count);
          });

        case "pop":
          return new NativeFunctionValue("pop", 0, arguments =>
          {
            if (array.Items.Count == 0)
            {
              throw new RuntimeException(name, "Cannot 'pop' from an empty array.");
            }

            int last = array.Items.Count - 1;
            var value = array.Items[last];
            array.Items.RemoveAt(last);
            return value;
          });

        case "join":
          return new NativeFunctionValue("join", 1, arguments =>
          {
            if (!(arguments[0] is ArrayValue other))
            {
              throw new RuntimeException(name, $"'join' expects an array argument but got '{arguments[0].TypeName}'.");
            }

            var combined = new List<ALKScriptValue>(array.Items.Count + other.Items.Count);
            combined.AddRange(array.Items);
            combined.AddRange(other.Items);
            return new ArrayValue(combined);
          });

        case "slice":
          return new NativeFunctionValue("slice", 2, arguments =>
          {
            int start = ExpectNonNegativeInt(arguments[0], name, "slice");
            int count = ExpectNonNegativeInt(arguments[1], name, "slice");

            if (start > array.Items.Count || start + count > array.Items.Count)
            {
              throw new RuntimeException(name, $"'slice' range [{start}, {start + count}) is out of bounds for an array of length {array.Items.Count}.");
            }

            return new ArrayValue(array.Items.GetRange(start, count));
          });

        case "remove":
          return new NativeFunctionValue("remove", 1, arguments =>
          {
            int index = ExpectIndex(arguments[0], name, array.Items.Count, "Array");

            var removed = array.Items[index];
            array.Items.RemoveAt(index);
            return removed;
          });

        case "map":
          return new NativeAsyncFunctionValue("map", 1, async arguments =>
          {
            var callback = ExpectCallable(arguments[0], name, "map");

            var results = new List<ALKScriptValue>(array.Items.Count);
            foreach (var item in array.Items)
            {
              var mapped = await _context.Call(callback, new List<ALKScriptValue> { item }, name);
              if (_context.Signal != null)
              {
                return NullValue.Instance;
              }

              results.Add(mapped);
            }

            return new ArrayValue(results);
          });

        case "filter":
          return new NativeAsyncFunctionValue("filter", 1, async arguments =>
          {
            var callback = ExpectCallable(arguments[0], name, "filter");

            var results = new List<ALKScriptValue>();
            foreach (var item in array.Items)
            {
              var keep = await _context.Call(callback, new List<ALKScriptValue> { item }, name);
              if (_context.Signal != null)
              {
                return NullValue.Instance;
              }

              if (!(keep is BoolValue boolValue))
              {
                throw new RuntimeException(name, $"'filter' callback must return a 'bool', but got '{keep.TypeName}'.");
              }

              if (boolValue.Value)
              {
                results.Add(item);
              }
            }

            return new ArrayValue(results);
          });

        default:
          throw new RuntimeException(name, $"Undefined property '{name.Lexeme}' on '{array.TypeName}'.");
      }
    }

    /// <summary>
    /// Resolves built-in string members: the <c>length</c> property and the
    /// <c>toUpper</c>/<c>toLower</c>/<c>trim</c>/<c>substring</c>/<c>indexOf</c>/
    /// <c>contains</c>/<c>startsWith</c>/<c>endsWith</c>/<c>split</c>/<c>replace</c>
    /// native methods. Strings are immutable, so all of these return new
    /// values without modifying <paramref name="value"/>.
    /// </summary>
    private static ALKScriptValue GetStringMember(StringValue value, ALKScriptToken name)
    {
      string self = value.Value;

      switch (name.Lexeme)
      {
        case "length":
          return new IntValue(self.Length);

        case "toUpper":
          return new NativeFunctionValue("toUpper", 0, arguments => new StringValue(self.ToUpperInvariant()));

        case "toLower":
          return new NativeFunctionValue("toLower", 0, arguments => new StringValue(self.ToLowerInvariant()));

        case "trim":
          return new NativeFunctionValue("trim", 0, arguments => new StringValue(self.Trim()));

        case "substring":
          return new NativeFunctionValue("substring", 2, arguments =>
          {
            int start = ExpectNonNegativeInt(arguments[0], name, "substring");
            int count = ExpectNonNegativeInt(arguments[1], name, "substring");

            if (start > self.Length || start + count > self.Length)
            {
              throw new RuntimeException(name, $"'substring' range [{start}, {start + count}) is out of bounds for a string of length {self.Length}.");
            }

            return new StringValue(self.Substring(start, count));
          });

        case "indexOf":
          return new NativeFunctionValue("indexOf", 1, arguments =>
            new IntValue(self.IndexOf(ExpectString(arguments[0], name, "indexOf"), StringComparison.Ordinal)));

        case "contains":
          return new NativeFunctionValue("contains", 1, arguments =>
            self.IndexOf(ExpectString(arguments[0], name, "contains"), StringComparison.Ordinal) >= 0 ? BoolValue.True : BoolValue.False);

        case "startsWith":
          return new NativeFunctionValue("startsWith", 1, arguments =>
            self.StartsWith(ExpectString(arguments[0], name, "startsWith"), StringComparison.Ordinal) ? BoolValue.True : BoolValue.False);

        case "endsWith":
          return new NativeFunctionValue("endsWith", 1, arguments =>
            self.EndsWith(ExpectString(arguments[0], name, "endsWith"), StringComparison.Ordinal) ? BoolValue.True : BoolValue.False);

        case "split":
          return new NativeFunctionValue("split", 1, arguments =>
          {
            string separator = ExpectString(arguments[0], name, "split");
            var parts = self.Split(new[] { separator }, StringSplitOptions.None);
            var items = new List<ALKScriptValue>(parts.Length);
            foreach (var part in parts)
            {
              items.Add(new StringValue(part));
            }

            return new ArrayValue(items);
          });

        case "replace":
          return new NativeFunctionValue("replace", 2, arguments =>
          {
            string oldValue = ExpectString(arguments[0], name, "replace");
            string newValue = ExpectString(arguments[1], name, "replace");
            return new StringValue(self.Replace(oldValue, newValue));
          });

        default:
          throw new RuntimeException(name, $"Undefined property '{name.Lexeme}' on '{value.TypeName}'.");
      }
    }

    private static string ExpectString(ALKScriptValue value, ALKScriptToken site, string memberName)
    {
      if (!(value is StringValue stringValue))
      {
        throw new RuntimeException(site, $"'{memberName}' expects a string argument but got '{value.TypeName}'.");
      }

      return stringValue.Value;
    }

    private static int ExpectNonNegativeInt(ALKScriptValue value, ALKScriptToken site, string memberName)
    {
      if (!(value is IntValue intValue) || intValue.Value < 0)
      {
        throw new RuntimeException(site, $"'{memberName}' expects non-negative int arguments but got '{value}'.");
      }

      return (int)intValue.Value;
    }

    private static CallableValue ExpectCallable(ALKScriptValue value, ALKScriptToken site, string memberName)
    {
      if (!(value is CallableValue callable) || callable.Arity != 1)
      {
        throw new RuntimeException(site, $"'{memberName}' expects a callback of arity 1 but got '{value.TypeName}'.");
      }

      return callable;
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

      return await _context.Construct(classValue, arguments, expression.TypeArguments, expression.Keyword);
    }
  }
}
