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
          var fieldMemberForWrite = instance.Class.FindMember(get.Name.Lexeme, out var fieldWriteDeclaringClass);
          if (fieldMemberForWrite != null)
          {
            EnforceAccessModifier(fieldMemberForWrite, fieldWriteDeclaringClass, get.Name, environment);
          }
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
          var instance = target as InstanceValue
            ?? throw new RuntimeException(get.Name, $"Cannot apply '{op.Lexeme}' to a value of type '{target.TypeName}'.");
          if (!instance.Fields.TryGetValue(get.Name.Lexeme, out var old))
            throw new RuntimeException(get.Name, $"Undefined field '{get.Name.Lexeme}'.");
          var next = Step(old, op);
          instance.Fields[get.Name.Lexeme] = next;
          return (old, next);
        }

        case IndexExpr index:
        {
          var target = await Eval(index.Target, environment);
          if (_context.Signal != null)
            return (NullValue.Instance, NullValue.Instance);
          var array = target as ArrayValue
            ?? throw new RuntimeException(index.ClosingBracket, $"Cannot index into a value of type '{target.TypeName}'.");
          var indexValue = await Eval(index.Index, environment);
          if (_context.Signal != null)
            return (NullValue.Instance, NullValue.Instance);
          int position = ExpectIndex(indexValue, index.ClosingBracket, array.Items.Count);
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

      switch (operand)
      {
        case TaskValue taskValue:
          return await AwaitTask(taskValue.Task);

        case PendingOperationValue pending:
          return await AwaitPending(pending);

        case ArrayValue array:
          return await EvalWhenAll(array.Items);

        default:
          return operand;
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

          case TaskValue tv:
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
          if (member is MethodDecl method)
          {
            EnforceAccessModifier(member, declaringClass, name, environment);
            return _functionValueFactory.CreateMethod(method, declaringClass!, ClassEnvironments.For(instance.Class), instance);
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
          if (staticMember is MethodDecl staticMethod)
          {
            EnforceAccessModifier(staticMember, staticDeclaringClass, name, environment);
            return _functionValueFactory.CreateMethod(staticMethod, staticDeclaringClass!, ClassEnvironments.For(classValue), boundInstance: null);
          }

          throw new RuntimeException(name, $"Undefined static member '{name.Lexeme}' on '{target.TypeName}'.");

        case ArrayValue array:
          return GetArrayMember(array, name);

        default:
          throw new RuntimeException(name, $"Cannot access property '{name.Lexeme}' on a value of type '{target.TypeName}'.");
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

    private async Task<ALKScriptValue> EvalCompoundAssignment(CompoundAssignmentExpr expression, ScriptEnvironment environment)
    {
      switch (expression.Target)
      {
        case IdentifierExpr identifier:
        {
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
          var instance = target as InstanceValue
            ?? throw new RuntimeException(get.Name, $"Cannot apply '{expression.Operator.Lexeme}' to a value of type '{target.TypeName}'.");
          if (!instance.Fields.TryGetValue(get.Name.Lexeme, out var current))
            throw new RuntimeException(get.Name, $"Undefined field '{get.Name.Lexeme}'.");
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
          var array = target as ArrayValue
            ?? throw new RuntimeException(index.ClosingBracket, $"Cannot index into a value of type '{target.TypeName}'.");
          var indexValue = await Eval(index.Index, environment);
          if (_context.Signal != null) return NullValue.Instance;
          int position = ExpectIndex(indexValue, index.ClosingBracket, array.Items.Count);
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

    /// <summary>
    /// Resolves built-in array members: the <c>length</c> property and the
    /// <c>push</c>/<c>pop</c>/<c>join</c>/<c>slice</c>/<c>remove</c> native
    /// methods. <c>push</c>, <c>pop</c>, and <c>remove</c> mutate
    /// <paramref name="array"/> in place; <c>join</c> and <c>slice</c> return
    /// new arrays without modifying the receiver.
    /// </summary>
    private static ALKScriptValue GetArrayMember(ArrayValue array, ALKScriptToken name)
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
            int index = ExpectIndex(arguments[0], name, array.Items.Count);

            var removed = array.Items[index];
            array.Items.RemoveAt(index);
            return removed;
          });

        default:
          throw new RuntimeException(name, $"Undefined property '{name.Lexeme}' on '{array.TypeName}'.");
      }
    }

    private static int ExpectNonNegativeInt(ALKScriptValue value, ALKScriptToken site, string memberName)
    {
      if (!(value is IntValue intValue) || intValue.Value < 0)
      {
        throw new RuntimeException(site, $"'{memberName}' expects non-negative int arguments but got '{value}'.");
      }

      return (int)intValue.Value;
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
