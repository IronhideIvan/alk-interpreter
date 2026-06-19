using System;
using System.Collections.Generic;
using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Common.Evaluation;
using ALKScript.Interpreter.Common.Evaluation.Scheduling;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Evaluator.Cursor
{
  /// <summary>
  /// Cursor-evaluator counterpart to <see cref="ExpressionEvaluator"/> (Step 1
  /// of the cursor-rewrite plan): literals, identifiers, <c>this</c>/<c>base</c>,
  /// grouping, array literals, binary/unary/ternary operators, and member
  /// access/indexing. None of these node types can themselves suspend, so
  /// every <see cref="StepResult"/> returned here is <see cref="StepResult.Completed"/> —
  /// but sub-expressions are still routed through <see cref="EvaluationCursor.Eval"/>
  /// and propagated with the mechanical "if (step.IsAwaiting) return step;"
  /// pattern, so that node types added in later steps (which CAN suspend)
  /// compose correctly through these.
  ///
  /// Node types not yet covered (assignment, calls, await, etc.) throw
  /// <see cref="RuntimeException"/> — they're added in subsequent steps.
  /// </summary>
  internal sealed class CursorExpressionEvaluator
  {
    private readonly EvaluationCursor _cursor;
    private readonly IFunctionValueFactory _functionValueFactory;

    public CursorExpressionEvaluator(EvaluationCursor cursor, IFunctionValueFactory functionValueFactory)
    {
      _cursor = cursor;
      _functionValueFactory = functionValueFactory;
    }

    public StepResult Eval(Expr expression, ScriptEnvironment environment, bool allowSuspend = false)
    {
      switch (expression)
      {
        case AwaitExpr awaitExpr:
          return EvalAwait(awaitExpr, environment, allowSuspend);

        case TypeofExpr typeofExpr:
          return EvalTypeof(typeofExpr, environment);

        case LiteralExpr literal:
          return StepResult.Completed(EvalLiteral(literal));

        case IdentifierExpr identifier:
          return StepResult.Completed(Names.LookUp(identifier.Name, environment));

        case ThisExpr thisExpr:
          return StepResult.Completed(Names.LookUp(thisExpr.Keyword, environment));

        case BaseExpr baseExpr:
          return StepResult.Completed(Names.LookUp(baseExpr.Keyword, environment));

        case GroupingExpr grouping:
          return _cursor.Eval(grouping.Expression, environment, allowSuspend);

        case ArrayLiteralExpr arrayLiteral:
          return EvalArrayLiteral(arrayLiteral, environment);

        case BinaryExpr binary:
          return EvalBinary(binary, environment);

        case UnaryExpr unary:
          return EvalUnary(unary, environment);

        case TernaryExpr ternary:
          return EvalTernary(ternary, environment);

        case GetExpr get:
          return EvalGet(get, environment);

        case NullConditionalGetExpr nullCondGet:
          return EvalNullConditionalGet(nullCondGet, environment);

        case IndexExpr index:
          return EvalIndex(index, environment);

        case AssignmentExpr assignment:
          return EvalAssignment(assignment, environment);

        case CallExpr call:
          return EvalCall(call, environment);

        case NewExpr newExpr:
          return EvalNew(newExpr, environment);

        case PrefixUpdateExpr prefixUpdate:
          return EvalPrefixUpdate(prefixUpdate, environment);

        case PostfixUpdateExpr postfixUpdate:
          return EvalPostfixUpdate(postfixUpdate, environment);

        case CompoundAssignmentExpr compound:
          return EvalCompoundAssignment(compound, environment);

        case InterpolatedStringExpr interpolated:
          return EvalInterpolatedString(interpolated, environment);

        case TypeTestExpr typeTest:
          return EvalTypeTest(typeTest, environment);

        case TypeCastExpr typeCast:
          return EvalTypeCast(typeCast, environment);

        case CastExpr cast:
          return EvalCast(cast, environment);

        case LambdaExpr lambda:
          return StepResult.Completed(EvalLambda(lambda, environment));

        case MapLiteralExpr mapLiteral:
          return EvalMapLiteral(mapLiteral, environment);

        default:
          throw new RuntimeException(
            AstTokenLocator.Of(expression),
            $"Evaluation of '{expression.GetType().Name}' is not yet supported by the cursor evaluator.");
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

    private StepResult EvalArrayLiteral(ArrayLiteralExpr expression, ScriptEnvironment environment)
    {
      var items = new List<ALKScriptValue>(expression.Elements.Count);

      foreach (var element in expression.Elements)
      {
        var step = _cursor.Eval(element, environment);
        if (step.IsAwaiting) return step;
        items.Add(step.Value!);

        if (_cursor.Signal != null)
        {
          return StepResult.Completed(NullValue.Instance);
        }
      }

      return StepResult.Completed(new ArrayValue(items));
    }

    private StepResult EvalBinary(BinaryExpr expression, ScriptEnvironment environment)
    {
      // Null-coalescing: left ?? right — return left if non-null, else right.
      if (expression.Operator.Type == ALKScriptTokenType.QuestionQuestion)
      {
        var leftStep = _cursor.Eval(expression.Left, environment);
        if (leftStep.IsAwaiting) return leftStep;
        var left = leftStep.Value!;
        if (_cursor.Signal != null)
        {
          return StepResult.Completed(NullValue.Instance);
        }
        if (!(left is NullValue))
        {
          return StepResult.Completed(left);
        }
        return _cursor.Eval(expression.Right, environment);
      }

      // Short-circuiting logical operators evaluate their right-hand side lazily.
      if (expression.Operator.Type == ALKScriptTokenType.AmpAmp)
      {
        var leftStep = _cursor.Eval(expression.Left, environment);
        if (leftStep.IsAwaiting) return leftStep;
        var left = leftStep.Value!;
        if (_cursor.Signal != null)
        {
          return StepResult.Completed(NullValue.Instance);
        }
        return left.IsTruthy ? _cursor.Eval(expression.Right, environment) : StepResult.Completed(left);
      }

      if (expression.Operator.Type == ALKScriptTokenType.PipePipe)
      {
        var leftStep = _cursor.Eval(expression.Left, environment);
        if (leftStep.IsAwaiting) return leftStep;
        var left = leftStep.Value!;
        if (_cursor.Signal != null)
        {
          return StepResult.Completed(NullValue.Instance);
        }
        return left.IsTruthy ? StepResult.Completed(left) : _cursor.Eval(expression.Right, environment);
      }

      var leftValueStep = _cursor.Eval(expression.Left, environment);
      if (leftValueStep.IsAwaiting) return leftValueStep;
      var leftValue = leftValueStep.Value!;
      if (_cursor.Signal != null)
      {
        return StepResult.Completed(NullValue.Instance);
      }

      var rightValueStep = _cursor.Eval(expression.Right, environment);
      if (rightValueStep.IsAwaiting) return rightValueStep;
      var rightValue = rightValueStep.Value!;
      if (_cursor.Signal != null)
      {
        return StepResult.Completed(NullValue.Instance);
      }

      var op = expression.Operator;

      // Check for operator overloads on either operand's class
      var overloadStep = TryInvokeOperatorOverload(leftValue, rightValue, op.Lexeme, op);
      if (overloadStep.HasValue) return overloadStep.Value;

      switch (op.Type)
      {
        case ALKScriptTokenType.Plus:
          return StepResult.Completed(Operators.Add(leftValue, rightValue, op));
        case ALKScriptTokenType.Minus:
          return StepResult.Completed(Operators.Arithmetic(leftValue, rightValue, op, (a, b) => a - b, (a, b) => a - b));
        case ALKScriptTokenType.Star:
          return StepResult.Completed(Operators.Arithmetic(leftValue, rightValue, op, (a, b) => a * b, (a, b) => a * b));
        case ALKScriptTokenType.Slash:
          return StepResult.Completed(Operators.Arithmetic(leftValue, rightValue, op, (a, b) => a / b, (a, b) => a / b));
        case ALKScriptTokenType.Percent:
          return StepResult.Completed(Operators.Arithmetic(leftValue, rightValue, op, (a, b) => a % b, (a, b) => a % b));

        case ALKScriptTokenType.Less:
          return StepResult.Completed(BoolValue.Of(Operators.Compare(leftValue, rightValue, op) < 0));
        case ALKScriptTokenType.LessEqual:
          return StepResult.Completed(BoolValue.Of(Operators.Compare(leftValue, rightValue, op) <= 0));
        case ALKScriptTokenType.Greater:
          return StepResult.Completed(BoolValue.Of(Operators.Compare(leftValue, rightValue, op) > 0));
        case ALKScriptTokenType.GreaterEqual:
          return StepResult.Completed(BoolValue.Of(Operators.Compare(leftValue, rightValue, op) >= 0));

        case ALKScriptTokenType.EqualEqual:
          return StepResult.Completed(BoolValue.Of(Operators.AreEqual(leftValue, rightValue)));
        case ALKScriptTokenType.BangEqual:
          return StepResult.Completed(BoolValue.Of(!Operators.AreEqual(leftValue, rightValue)));

        case ALKScriptTokenType.Amp:
          return StepResult.Completed(Operators.Bitwise(leftValue, rightValue, op, (a, b) => a & b));
        case ALKScriptTokenType.Pipe:
          return StepResult.Completed(Operators.Bitwise(leftValue, rightValue, op, (a, b) => a | b));
        case ALKScriptTokenType.Caret:
          return StepResult.Completed(Operators.Bitwise(leftValue, rightValue, op, (a, b) => a ^ b));
        case ALKScriptTokenType.LessLess:
          return StepResult.Completed(Operators.Bitwise(leftValue, rightValue, op, (a, b) => a << (int)b));
        case ALKScriptTokenType.GreaterGreater:
          return StepResult.Completed(Operators.Bitwise(leftValue, rightValue, op, (a, b) => a >> (int)b));

        default:
          throw new RuntimeException(op, $"Unsupported binary operator '{op.Lexeme}'.");
      }
    }

    private StepResult EvalUnary(UnaryExpr expression, ScriptEnvironment environment)
    {
      var operandStep = _cursor.Eval(expression.Operand, environment);
      if (operandStep.IsAwaiting) return operandStep;
      var operand = operandStep.Value!;

      if (_cursor.Signal != null)
      {
        return StepResult.Completed(NullValue.Instance);
      }

      switch (expression.Operator.Type)
      {
        case ALKScriptTokenType.Bang:
          return StepResult.Completed(BoolValue.Of(!operand.IsTruthy));

        case ALKScriptTokenType.Minus:
        {
          // Check for unary operator overload
          var unaryOverloadStep = TryInvokeUnaryOperatorOverload(operand, "-", expression.Operator);
          if (unaryOverloadStep.HasValue) return unaryOverloadStep.Value;

          switch (operand)
          {
            case IntValue intValue:
              return StepResult.Completed(new IntValue(-intValue.Value));
            case FloatValue floatValue:
              return StepResult.Completed(new FloatValue(-floatValue.Value));
            default:
              throw new RuntimeException(expression.Operator, $"Operator '-' cannot be applied to '{operand.TypeName}'.");
          }
        }

        case ALKScriptTokenType.Tilde:
          switch (operand)
          {
            case IntValue intValue:
              return StepResult.Completed(new IntValue(~intValue.Value));
            default:
              throw new RuntimeException(expression.Operator, $"Operator '~' cannot be applied to '{operand.TypeName}'; bitwise operators require 'int' operands.");
          }

        default:
          throw new RuntimeException(expression.Operator, $"Unsupported unary operator '{expression.Operator.Lexeme}'.");
      }
    }

    private StepResult EvalTernary(TernaryExpr expression, ScriptEnvironment environment)
    {
      var conditionStep = _cursor.Eval(expression.Condition, environment);
      if (conditionStep.IsAwaiting) return conditionStep;
      var condition = conditionStep.Value!;

      if (_cursor.Signal != null)
      {
        return StepResult.Completed(NullValue.Instance);
      }

      return condition.IsTruthy
        ? _cursor.Eval(expression.ThenExpr, environment)
        : _cursor.Eval(expression.ElseExpr, environment);
    }

    private StepResult EvalGet(GetExpr expression, ScriptEnvironment environment)
    {
      var targetStep = _cursor.Eval(expression.Target, environment);
      if (targetStep.IsAwaiting) return targetStep;
      var target = targetStep.Value!;

      if (_cursor.Signal != null)
      {
        return StepResult.Completed(NullValue.Instance);
      }

      // Check for property getter before falling through to GetMember
      if (target is InstanceValue getInst)
      {
        var propMember = getInst.Class.FindMember(expression.Name.Lexeme, out var propClass);
        if (propMember is PropertyDecl propDecl && !propDecl.IsStatic)
        {
          EnforceAccessModifier(propDecl, propClass, expression.Name, environment);
          return InvokePropertyGetter(propDecl, propClass!, getInst, expression.Name, environment);
        }
      }
      else if (target is ClassValue staticClassGet)
      {
        var staticPropMember = staticClassGet.FindMember(expression.Name.Lexeme, out var staticPropClass);
        if (staticPropMember is PropertyDecl staticPropDecl && staticPropDecl.IsStatic)
        {
          EnforceAccessModifier(staticPropDecl, staticPropClass, expression.Name, environment);
          return InvokeStaticPropertyGetter(staticPropDecl, staticClassGet, expression.Name, environment);
        }
      }

      return StepResult.Completed(GetMember(target, expression.Name, environment));
    }

    private StepResult EvalNullConditionalGet(NullConditionalGetExpr expression, ScriptEnvironment environment)
    {
      var targetStep = _cursor.Eval(expression.Target, environment);
      if (targetStep.IsAwaiting) return targetStep;
      var target = targetStep.Value!;

      if (_cursor.Signal != null)
      {
        return StepResult.Completed(NullValue.Instance);
      }

      // Short-circuit on null — the whole expression becomes null.
      if (target is NullValue)
      {
        return StepResult.Completed(NullValue.Instance);
      }

      // Check for property getter before falling through to GetMember
      if (target is InstanceValue ncInst)
      {
        var ncPropMember = ncInst.Class.FindMember(expression.Name.Lexeme, out var ncPropClass);
        if (ncPropMember is PropertyDecl ncPropDecl && !ncPropDecl.IsStatic)
        {
          EnforceAccessModifier(ncPropDecl, ncPropClass, expression.Name, environment);
          return InvokePropertyGetter(ncPropDecl, ncPropClass!, ncInst, expression.Name, environment);
        }
      }

      return StepResult.Completed(GetMember(target, expression.Name, environment));
    }

    private StepResult EvalIndex(IndexExpr expression, ScriptEnvironment environment)
    {
      var targetStep = _cursor.Eval(expression.Target, environment);
      if (targetStep.IsAwaiting) return targetStep;
      var target = targetStep.Value!;

      if (_cursor.Signal != null)
      {
        return StepResult.Completed(NullValue.Instance);
      }

      var indexStep = _cursor.Eval(expression.Index, environment);
      if (indexStep.IsAwaiting) return indexStep;
      var indexValue = indexStep.Value!;

      if (_cursor.Signal != null)
      {
        return StepResult.Completed(NullValue.Instance);
      }

      switch (target)
      {
        case ArrayValue array:
          int position = ExpectIndex(indexValue, expression.ClosingBracket, array.Items.Count, "Array");
          return StepResult.Completed(array.Items[position]);

        case StringValue stringValue:
          int charPosition = ExpectIndex(indexValue, expression.ClosingBracket, stringValue.Value.Length, "String");
          return StepResult.Completed(new StringValue(stringValue.Value[charPosition].ToString()));

        case MapValue map:
          ValidateMapKey(indexValue, expression.ClosingBracket);
          if (!map.Entries.TryGetValue(indexValue, out var mapValue))
          {
            throw new RuntimeException(expression.ClosingBracket, $"Key not found in map.");
          }
          return StepResult.Completed(mapValue);

        default:
          throw new RuntimeException(expression.ClosingBracket, $"Cannot index into a value of type '{target.TypeName}'.");
      }
    }

    private StepResult EvalAssignment(AssignmentExpr expression, ScriptEnvironment environment)
    {
      var valueStep = _cursor.Eval(expression.Value, environment);
      if (valueStep.IsAwaiting) return valueStep;
      var value = valueStep.Value!;

      if (_cursor.Signal != null)
      {
        return StepResult.Completed(NullValue.Instance);
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
          return StepResult.Completed(value);

        case GetExpr get:
        {
          var targetStep = _cursor.Eval(get.Target, environment);
          if (targetStep.IsAwaiting) return targetStep;
          var target = targetStep.Value!;

          if (_cursor.Signal != null)
          {
            return StepResult.Completed(NullValue.Instance);
          }

          if (target is ClassValue staticTargetClass)
          {
            // Check for static property setter
            var staticPropMemberForWrite = staticTargetClass.FindMember(get.Name.Lexeme, out var staticPropWriteClass);
            if (staticPropMemberForWrite is PropertyDecl staticPropForWrite && staticPropForWrite.IsStatic)
            {
              EnforceAccessModifier(staticPropForWrite, staticPropWriteClass, get.Name, environment);
              return InvokeStaticPropertySetter(staticPropForWrite, staticPropWriteClass!, get.Name, value, environment);
            }

            var (staticDeclaringClass, staticField) = ResolveStaticField(staticTargetClass, get.Name, environment);
            TypeChecking.EnsureAssignable(staticField.Type, value, get.Name, $"static field '{get.Name.Lexeme}'", environment);
            staticDeclaringClass.StaticFields[get.Name.Lexeme] = value;
            return StepResult.Completed(value);
          }

          var instance = target as InstanceValue
            ?? throw new RuntimeException(get.Name, $"Cannot set property '{get.Name.Lexeme}' on a value of type '{target.TypeName}'.");

          // Check for property setter
          var writePropertyMember = instance.Class.FindMember(get.Name.Lexeme, out var writePropertyClass);
          if (writePropertyMember is PropertyDecl writePropertyDecl && !writePropertyDecl.IsStatic)
          {
            EnforceAccessModifier(writePropertyDecl, writePropertyClass, get.Name, environment);
            return InvokePropertySetter(writePropertyDecl, writePropertyClass!, instance, get.Name, value, environment);
          }

          var fieldMemberForWrite = writePropertyMember;
          var fieldWriteDeclaringClass = writePropertyClass;
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
          return StepResult.Completed(value);
        }

        case IndexExpr index:
        {
          var indexedStep = _cursor.Eval(index.Target, environment);
          if (indexedStep.IsAwaiting) return indexedStep;
          var indexed = indexedStep.Value!;

          if (_cursor.Signal != null)
          {
            return StepResult.Completed(NullValue.Instance);
          }

          if (indexed is StringValue)
          {
            throw new RuntimeException(index.ClosingBracket, "Cannot assign to a string index; strings are immutable.");
          }

          if (indexed is MapValue mapTarget)
          {
            var mapKeyStep = _cursor.Eval(index.Index, environment);
            if (mapKeyStep.IsAwaiting) return mapKeyStep;
            var mapKey = mapKeyStep.Value!;

            if (_cursor.Signal != null) return StepResult.Completed(NullValue.Instance);

            ValidateMapKey(mapKey, index.ClosingBracket);
            mapTarget.Entries[mapKey] = value;
            return StepResult.Completed(value);
          }

          var array = indexed as ArrayValue
            ?? throw new RuntimeException(index.ClosingBracket, $"Cannot index into a value of type '{indexed.TypeName}'.");

          var indexValueStep = _cursor.Eval(index.Index, environment);
          if (indexValueStep.IsAwaiting) return indexValueStep;
          var indexValue = indexValueStep.Value!;

          if (_cursor.Signal != null)
          {
            return StepResult.Completed(NullValue.Instance);
          }

          if (array.IsFrozen) throw new RuntimeException(index.ClosingBracket, "Cannot assign to an index of a 'const' array.");
          int position = ExpectIndex(indexValue, index.ClosingBracket, array.Items.Count, "Array");
          array.Items[position] = value;
          return StepResult.Completed(value);
        }

        default:
          throw new RuntimeException(AstTokenLocator.Of(expression.Target), "Invalid assignment target.");
      }
    }

    private StepResult EvalCall(CallExpr expression, ScriptEnvironment environment)
    {
      var calleeStep = _cursor.Eval(expression.Callee, environment);
      if (calleeStep.IsAwaiting) return calleeStep;
      var callee = calleeStep.Value!;

      if (_cursor.Signal != null)
      {
        return StepResult.Completed(NullValue.Instance);
      }

      // null-conditional short-circuit: obj?.method(...) → null when obj is null
      if (callee is NullValue && expression.Callee is NullConditionalGetExpr)
      {
        return StepResult.Completed(NullValue.Instance);
      }

      var arguments = new List<ALKScriptValue>(expression.Arguments.Count);
      foreach (var argument in expression.Arguments)
      {
        var argumentStep = _cursor.Eval(argument, environment);
        if (argumentStep.IsAwaiting) return argumentStep;
        arguments.Add(argumentStep.Value!);

        if (_cursor.Signal != null)
        {
          return StepResult.Completed(NullValue.Instance);
        }
      }

      return _cursor.Call(callee, arguments, expression.ClosingParen);
    }

    private StepResult EvalNew(NewExpr expression, ScriptEnvironment environment)
    {
      var callee = Names.LookUp(expression.TypeName, environment);
      var classValue = callee as ClassValue
        ?? throw new RuntimeException(expression.TypeName, $"'{expression.TypeName.Lexeme}' is not a class.");

      var arguments = new List<ALKScriptValue>(expression.Arguments.Count);
      foreach (var argument in expression.Arguments)
      {
        var argumentStep = _cursor.Eval(argument, environment);
        if (argumentStep.IsAwaiting) return argumentStep;
        arguments.Add(argumentStep.Value!);

        if (_cursor.Signal != null)
        {
          return StepResult.Completed(NullValue.Instance);
        }
      }

      return _cursor.Construct(classValue, arguments, expression.TypeArguments, expression.Keyword);
    }

    private StepResult EvalPrefixUpdate(PrefixUpdateExpr expression, ScriptEnvironment environment)
    {
      var suspend = EvalUpdate(expression.Operand, expression.Operator, environment, out _, out var newValue);
      if (suspend != null) return suspend.Value;
      return StepResult.Completed(newValue);
    }

    private StepResult EvalPostfixUpdate(PostfixUpdateExpr expression, ScriptEnvironment environment)
    {
      var suspend = EvalUpdate(expression.Operand, expression.Operator, environment, out var oldValue, out _);
      if (suspend != null) return suspend.Value;
      return StepResult.Completed(oldValue);
    }

    private StepResult? EvalUpdate(Expr operand, ALKScriptToken op, ScriptEnvironment environment, out ALKScriptValue oldValue, out ALKScriptValue newValue)
    {
      oldValue = NullValue.Instance;
      newValue = NullValue.Instance;

      switch (operand)
      {
        case IdentifierExpr identifier:
        {
          if (environment.IsConst(identifier.Name.Lexeme))
          {
            throw new RuntimeException(identifier.Name, $"Cannot assign to 'const' variable '{identifier.Name.Lexeme}'.");
          }
          oldValue = Names.LookUp(identifier.Name, environment);
          newValue = Step(oldValue, op);
          if (!environment.TryAssign(identifier.Name.Lexeme, newValue))
            throw new RuntimeException(identifier.Name, $"Undefined name '{identifier.Name.Lexeme}'.");
          return null;
        }

        case GetExpr get:
        {
          var targetStep = _cursor.Eval(get.Target, environment);
          if (targetStep.IsAwaiting) return targetStep;
          var target = targetStep.Value!;

          if (_cursor.Signal != null)
          {
            return StepResult.Completed(NullValue.Instance);
          }

          if (target is ClassValue staticTargetClass)
          {
            var (staticDeclaringClass, _) = ResolveStaticField(staticTargetClass, get.Name, environment);
            oldValue = staticDeclaringClass.StaticFields[get.Name.Lexeme];
            newValue = Step(oldValue, op);
            staticDeclaringClass.StaticFields[get.Name.Lexeme] = newValue;
            return null;
          }

          var instance = target as InstanceValue
            ?? throw new RuntimeException(get.Name, $"Cannot apply '{op.Lexeme}' to a value of type '{target.TypeName}'.");
          if (!instance.Fields.TryGetValue(get.Name.Lexeme, out oldValue))
            throw new RuntimeException(get.Name, $"Undefined field '{get.Name.Lexeme}'.");
          var fieldMemberForUpdate = instance.Class.FindMember(get.Name.Lexeme, out var fieldUpdateDeclaringClass);
          EnforceFieldWritable(fieldMemberForUpdate, fieldUpdateDeclaringClass, get.Name, environment);
          newValue = Step(oldValue, op);
          instance.Fields[get.Name.Lexeme] = newValue;
          return null;
        }

        case IndexExpr index:
        {
          var targetStep = _cursor.Eval(index.Target, environment);
          if (targetStep.IsAwaiting) return targetStep;
          var target = targetStep.Value!;

          if (_cursor.Signal != null)
          {
            return StepResult.Completed(NullValue.Instance);
          }

          if (target is StringValue)
          {
            throw new RuntimeException(index.ClosingBracket, "Cannot assign to a string index; strings are immutable.");
          }

          if (target is MapValue mapUpdate)
          {
            var mapIdxStep = _cursor.Eval(index.Index, environment);
            if (mapIdxStep.IsAwaiting) return mapIdxStep;
            var mapIdx = mapIdxStep.Value!;
            if (_cursor.Signal != null) return StepResult.Completed(NullValue.Instance);
            ValidateMapKey(mapIdx, index.ClosingBracket);
            if (!mapUpdate.Entries.TryGetValue(mapIdx, out oldValue))
              throw new RuntimeException(index.ClosingBracket, "Key not found in map.");
            newValue = Step(oldValue, op);
            mapUpdate.Entries[mapIdx] = newValue;
            return null;
          }

          var array = target as ArrayValue
            ?? throw new RuntimeException(index.ClosingBracket, $"Cannot index into a value of type '{target.TypeName}'.");

          var indexStep = _cursor.Eval(index.Index, environment);
          if (indexStep.IsAwaiting) return indexStep;
          var indexValue = indexStep.Value!;

          if (_cursor.Signal != null)
          {
            return StepResult.Completed(NullValue.Instance);
          }

          if (array.IsFrozen) throw new RuntimeException(index.ClosingBracket, "Cannot assign to an index of a 'const' array.");
          int position = ExpectIndex(indexValue, index.ClosingBracket, array.Items.Count, "Array");
          oldValue = array.Items[position];
          newValue = Step(oldValue, op);
          array.Items[position] = newValue;
          return null;
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

    private StepResult EvalCompoundAssignment(CompoundAssignmentExpr expression, ScriptEnvironment environment)
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
          var rhsStep = _cursor.Eval(expression.Value, environment);
          if (rhsStep.IsAwaiting) return rhsStep;
          var rhs = rhsStep.Value!;
          if (_cursor.Signal != null) return StepResult.Completed(NullValue.Instance);
          var result = ApplyCompound(current, rhs, expression.Operator);
          if (!environment.TryAssign(identifier.Name.Lexeme, result))
            throw new RuntimeException(identifier.Name, $"Undefined name '{identifier.Name.Lexeme}'.");
          return StepResult.Completed(result);
        }

        case GetExpr get:
        {
          var targetStep = _cursor.Eval(get.Target, environment);
          if (targetStep.IsAwaiting) return targetStep;
          var target = targetStep.Value!;
          if (_cursor.Signal != null) return StepResult.Completed(NullValue.Instance);

          if (target is ClassValue staticTargetClass)
          {
            var (staticDeclaringClass, _) = ResolveStaticField(staticTargetClass, get.Name, environment);
            var staticCurrent = staticDeclaringClass.StaticFields[get.Name.Lexeme];
            var staticRhsStep = _cursor.Eval(expression.Value, environment);
            if (staticRhsStep.IsAwaiting) return staticRhsStep;
            var staticRhs = staticRhsStep.Value!;
            if (_cursor.Signal != null) return StepResult.Completed(NullValue.Instance);
            var staticResult = ApplyCompound(staticCurrent, staticRhs, expression.Operator);
            staticDeclaringClass.StaticFields[get.Name.Lexeme] = staticResult;
            return StepResult.Completed(staticResult);
          }

          var instance = target as InstanceValue
            ?? throw new RuntimeException(get.Name, $"Cannot apply '{expression.Operator.Lexeme}' to a value of type '{target.TypeName}'.");
          if (!instance.Fields.TryGetValue(get.Name.Lexeme, out var current))
            throw new RuntimeException(get.Name, $"Undefined field '{get.Name.Lexeme}'.");
          var fieldMemberForCompound = instance.Class.FindMember(get.Name.Lexeme, out var fieldCompoundDeclaringClass);
          EnforceFieldWritable(fieldMemberForCompound, fieldCompoundDeclaringClass, get.Name, environment);
          var rhsStep2 = _cursor.Eval(expression.Value, environment);
          if (rhsStep2.IsAwaiting) return rhsStep2;
          var rhs2 = rhsStep2.Value!;
          if (_cursor.Signal != null) return StepResult.Completed(NullValue.Instance);
          var result2 = ApplyCompound(current, rhs2, expression.Operator);
          instance.Fields[get.Name.Lexeme] = result2;
          return StepResult.Completed(result2);
        }

        case IndexExpr index:
        {
          var targetStep = _cursor.Eval(index.Target, environment);
          if (targetStep.IsAwaiting) return targetStep;
          var target = targetStep.Value!;
          if (_cursor.Signal != null) return StepResult.Completed(NullValue.Instance);

          if (target is StringValue)
          {
            throw new RuntimeException(index.ClosingBracket, "Cannot assign to a string index; strings are immutable.");
          }

          if (target is MapValue mapCompound)
          {
            var mapIdxStepC = _cursor.Eval(index.Index, environment);
            if (mapIdxStepC.IsAwaiting) return mapIdxStepC;
            var mapIdxC = mapIdxStepC.Value!;
            if (_cursor.Signal != null) return StepResult.Completed(NullValue.Instance);
            ValidateMapKey(mapIdxC, index.ClosingBracket);
            if (!mapCompound.Entries.TryGetValue(mapIdxC, out var currentMapVal))
              throw new RuntimeException(index.ClosingBracket, "Key not found in map.");
            var rhsMapStep = _cursor.Eval(expression.Value, environment);
            if (rhsMapStep.IsAwaiting) return rhsMapStep;
            var rhsMap = rhsMapStep.Value!;
            if (_cursor.Signal != null) return StepResult.Completed(NullValue.Instance);
            var resultMap = ApplyCompound(currentMapVal, rhsMap, expression.Operator);
            mapCompound.Entries[mapIdxC] = resultMap;
            return StepResult.Completed(resultMap);
          }

          var array = target as ArrayValue
            ?? throw new RuntimeException(index.ClosingBracket, $"Cannot index into a value of type '{target.TypeName}'.");

          var indexStep = _cursor.Eval(index.Index, environment);
          if (indexStep.IsAwaiting) return indexStep;
          var indexValue = indexStep.Value!;
          if (_cursor.Signal != null) return StepResult.Completed(NullValue.Instance);

          int position = ExpectIndex(indexValue, index.ClosingBracket, array.Items.Count, "Array");
          var current = array.Items[position];

          var rhsStep3 = _cursor.Eval(expression.Value, environment);
          if (rhsStep3.IsAwaiting) return rhsStep3;
          var rhs3 = rhsStep3.Value!;
          if (_cursor.Signal != null) return StepResult.Completed(NullValue.Instance);

          var result3 = ApplyCompound(current, rhs3, expression.Operator);
          array.Items[position] = result3;
          return StepResult.Completed(result3);
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

    private StepResult EvalInterpolatedString(InterpolatedStringExpr expression, ScriptEnvironment environment)
    {
      var builder = new System.Text.StringBuilder();

      builder.Append(expression.Parts[0]);

      for (int i = 0; i < expression.Expressions.Count; i++)
      {
        var step = _cursor.Eval(expression.Expressions[i], environment);
        if (step.IsAwaiting) return step;
        var value = step.Value!;

        if (_cursor.Signal != null)
        {
          return StepResult.Completed(NullValue.Instance);
        }

        builder.Append(Operators.Stringify(value));
        builder.Append(expression.Parts[i + 1]);
      }

      return StepResult.Completed(new StringValue(builder.ToString()));
    }

    private StepResult EvalTypeTest(TypeTestExpr expression, ScriptEnvironment environment)
    {
      var operandStep = _cursor.Eval(expression.Operand, environment);
      if (operandStep.IsAwaiting) return operandStep;
      var operand = operandStep.Value!;

      if (_cursor.Signal != null)
      {
        return StepResult.Completed(NullValue.Instance);
      }

      return StepResult.Completed(BoolValue.Of(TypeChecking.MatchesType(operand, expression.Type, environment, expression.Keyword)));
    }

    private StepResult EvalTypeCast(TypeCastExpr expression, ScriptEnvironment environment)
    {
      var operandStep = _cursor.Eval(expression.Operand, environment);
      if (operandStep.IsAwaiting) return operandStep;
      var operand = operandStep.Value!;

      if (_cursor.Signal != null)
      {
        return StepResult.Completed(NullValue.Instance);
      }

      return StepResult.Completed(TypeChecking.MatchesType(operand, expression.Type, environment, expression.Keyword) ? operand : NullValue.Instance);
    }

    private StepResult EvalCast(CastExpr expression, ScriptEnvironment environment)
    {
      var operandStep = _cursor.Eval(expression.Operand, environment);
      if (operandStep.IsAwaiting) return operandStep;
      var operand = operandStep.Value!;

      if (_cursor.Signal != null)
      {
        return StepResult.Completed(NullValue.Instance);
      }

      switch (expression.TargetType)
      {
        case "int":
        case "long":
          switch (operand)
          {
            case IntValue intValue:
              return StepResult.Completed(intValue);
            case FloatValue floatValue:
              return StepResult.Completed(new IntValue((long)floatValue.Value));
            default:
              throw new RuntimeException(expression.Keyword, $"Cannot cast a value of type '{operand.TypeName}' to '{expression.TargetType}'.");
          }

        case "float":
          switch (operand)
          {
            case FloatValue floatValue:
              return StepResult.Completed(floatValue);
            case IntValue intValue:
              return StepResult.Completed(new FloatValue(intValue.Value));
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
    private static ALKScriptValue EvalLambda(LambdaExpr lambda, ScriptEnvironment environment)
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

    internal static string BackingFieldName(string propertyName) => "__prop_" + propertyName;

    private StepResult? TryInvokeOperatorOverload(ALKScriptValue left, ALKScriptValue right, string opSymbol, ALKScriptToken site)
    {
      // For == and !=, handle the auto-negation rule
      if (opSymbol == "!=" )
      {
        var eqOverload = FindOperatorDecl(left, "==", 2) ?? FindOperatorDecl(right, "==", 2);
        if (eqOverload != null)
        {
          // Check if != is explicitly defined
          var neqOverload = FindOperatorDecl(left, "!=", 2) ?? FindOperatorDecl(right, "!=", 2);
          if (neqOverload == null)
          {
            // Auto-generate != as negation of ==
            var eqResult = InvokeOperatorDecl(eqOverload, new System.Collections.Generic.List<ALKScriptValue> { left, right }, site);
            if (eqResult.IsAwaiting) return eqResult;
            if (_cursor.Signal != null) return StepResult.Completed(NullValue.Instance);
            return StepResult.Completed(BoolValue.Of(!eqResult.Value!.IsTruthy));
          }
        }
      }

      var overload = FindOperatorDecl(left, opSymbol, 2) ?? FindOperatorDecl(right, opSymbol, 2);
      if (overload == null) return null;

      var result = InvokeOperatorDecl(overload, new System.Collections.Generic.List<ALKScriptValue> { left, right }, site);
      if (result.IsAwaiting) return result;
      return result;
    }

    private StepResult? TryInvokeUnaryOperatorOverload(ALKScriptValue operand, string opSymbol, ALKScriptToken site)
    {
      var overload = FindOperatorDecl(operand, opSymbol, 1);
      if (overload == null) return null;
      return InvokeOperatorDecl(overload, new System.Collections.Generic.List<ALKScriptValue> { operand }, site);
    }

    private static OperatorOverloadDecl? FindOperatorDecl(ALKScriptValue value, string opSymbol, int arity)
    {
      if (value is InstanceValue inst)
        return inst.Class.FindOperator(opSymbol, arity);
      return null;
    }

    private StepResult InvokeOperatorDecl(OperatorOverloadDecl overload, System.Collections.Generic.List<ALKScriptValue> arguments, ALKScriptToken site)
    {
      // Find the declaring class to get the right closure environment
      // We need to find which class declares this operator
      ClassValue? declaringClass = null;
      if (arguments.Count > 0 && arguments[0] is InstanceValue first)
      {
        for (ClassValue? c = first.Class; c != null; c = c.Superclass)
        {
          foreach (var m in c.Declaration.Members)
          {
            if (ReferenceEquals(m, overload)) { declaringClass = c; break; }
          }
          if (declaringClass != null) break;
        }
      }
      if (declaringClass == null && arguments.Count > 1 && arguments[1] is InstanceValue second)
      {
        for (ClassValue? c = second.Class; c != null; c = c.Superclass)
        {
          foreach (var m in c.Declaration.Members)
          {
            if (ReferenceEquals(m, overload)) { declaringClass = c; break; }
          }
          if (declaringClass != null) break;
        }
      }

      if (declaringClass == null)
        throw new RuntimeException(site, "Cannot resolve operator overload declaring class.");

      var opDecl = new FunctionDecl(false, System.Array.Empty<string>(), overload.ReturnType, overload.Operator, overload.Parameters, overload.Body);
      var opFunc = new FunctionValue(opDecl, ClassEnvironments.For(declaringClass), boundInstance: null, declaringClass);
      return _cursor.Call(opFunc, arguments, site);
    }

    private StepResult InvokePropertyGetter(PropertyDecl property, ClassValue declaringClass, InstanceValue instance, ALKScriptToken site, ScriptEnvironment environment)
    {
      if (!property.HasGetter)
        throw new RuntimeException(site, $"Property '{property.Name.Lexeme}' has no getter.");

      if (property.GetterBody == null)
      {
        // Auto-property getter: read from backing field
        var backingField = BackingFieldName(property.Name.Lexeme);
        return StepResult.Completed(instance.Fields.TryGetValue(backingField, out var v) ? v : NullValue.Instance);
      }

      // Full property getter: invoke as a zero-parameter function
      var getterDecl = new FunctionDecl(false, System.Array.Empty<string>(), property.Type, property.Name, System.Array.Empty<Parameter>(), property.GetterBody);
      var getter = new FunctionValue(getterDecl, ClassEnvironments.For(declaringClass), instance, declaringClass);
      return _cursor.Call(getter, new System.Collections.Generic.List<ALKScriptValue>(), site);
    }

    private StepResult InvokeStaticPropertyGetter(PropertyDecl property, ClassValue declaringClass, ALKScriptToken site, ScriptEnvironment environment)
    {
      if (!property.HasGetter)
        throw new RuntimeException(site, $"Property '{property.Name.Lexeme}' has no getter.");

      if (property.GetterBody == null)
      {
        var backingField = BackingFieldName(property.Name.Lexeme);
        return StepResult.Completed(declaringClass.StaticFields.TryGetValue(backingField, out var v) ? v : NullValue.Instance);
      }

      var getterDecl = new FunctionDecl(false, System.Array.Empty<string>(), property.Type, property.Name, System.Array.Empty<Parameter>(), property.GetterBody);
      var getter = new FunctionValue(getterDecl, ClassEnvironments.For(declaringClass), boundInstance: null, declaringClass);
      return _cursor.Call(getter, new System.Collections.Generic.List<ALKScriptValue>(), site);
    }

    private StepResult InvokePropertySetter(PropertyDecl property, ClassValue declaringClass, InstanceValue instance, ALKScriptToken site, ALKScriptValue value, ScriptEnvironment environment)
    {
      if (!property.HasSetter)
      {
        // Get-only auto-property: allow assignment from within the declaring class's constructor
        if (property.HasGetter && property.GetterBody == null && environment.IsInConstructor && environment.CurrentClass == declaringClass)
        {
          var backingField = BackingFieldName(property.Name.Lexeme);
          instance.Fields[backingField] = value;
          return StepResult.Completed(value);
        }
        throw new RuntimeException(site, $"Cannot assign to get-only property '{property.Name.Lexeme}'.");
      }

      if (property.SetterBody == null)
      {
        // Auto-property setter: write to backing field
        var backingField = BackingFieldName(property.Name.Lexeme);
        instance.Fields[backingField] = value;
        return StepResult.Completed(value);
      }

      // Full property setter: invoke with implicit 'value' parameter
      var valueParam = new Parameter(new TypeNode("var", System.Array.Empty<TypeNode>(), 0, false), "value");
      var setterDecl = new FunctionDecl(false, System.Array.Empty<string>(),
        new TypeNode("void", System.Array.Empty<TypeNode>(), 0, false),
        property.Name, new[] { valueParam }, property.SetterBody);
      var setter = new FunctionValue(setterDecl, ClassEnvironments.For(declaringClass), instance, declaringClass);
      var setterStep = _cursor.Call(setter, new System.Collections.Generic.List<ALKScriptValue> { value }, site);
      if (setterStep.IsAwaiting) return setterStep;
      return StepResult.Completed(value);
    }

    private StepResult InvokeStaticPropertySetter(PropertyDecl property, ClassValue declaringClass, ALKScriptToken site, ALKScriptValue value, ScriptEnvironment environment)
    {
      if (!property.HasSetter)
        throw new RuntimeException(site, $"Cannot assign to get-only static property '{property.Name.Lexeme}'.");

      if (property.SetterBody == null)
      {
        var backingField = BackingFieldName(property.Name.Lexeme);
        declaringClass.StaticFields[backingField] = value;
        return StepResult.Completed(value);
      }

      var valueParam = new Parameter(new TypeNode("var", System.Array.Empty<TypeNode>(), 0, false), "value");
      var setterDecl = new FunctionDecl(false, System.Array.Empty<string>(),
        new TypeNode("void", System.Array.Empty<TypeNode>(), 0, false),
        property.Name, new[] { valueParam }, property.SetterBody);
      var setter = new FunctionValue(setterDecl, ClassEnvironments.For(declaringClass), boundInstance: null, declaringClass);
      var setterStep = _cursor.Call(setter, new System.Collections.Generic.List<ALKScriptValue> { value }, site);
      if (setterStep.IsAwaiting) return setterStep;
      return StepResult.Completed(value);
    }

    private StepResult EvalMapLiteral(MapLiteralExpr expression, ScriptEnvironment environment)
    {
      var map = new MapValue(expression.KeyType, expression.ValueType);

      foreach (var (keyExpr, valueExpr) in expression.Entries)
      {
        var keyStep = _cursor.Eval(keyExpr, environment);
        if (keyStep.IsAwaiting) return keyStep;
        var key = keyStep.Value!;

        if (_cursor.Signal != null) return StepResult.Completed(NullValue.Instance);

        ValidateMapKey(key, expression.Keyword);

        var valueStep = _cursor.Eval(valueExpr, environment);
        if (valueStep.IsAwaiting) return valueStep;
        var value = valueStep.Value!;

        if (_cursor.Signal != null) return StepResult.Completed(NullValue.Instance);

        map.Entries[key] = value;
      }

      return StepResult.Completed(map);
    }

    private static void ValidateMapKey(ALKScriptValue key, ALKScriptToken site)
    {
      if (!(key is StringValue || key is IntValue || key is EnumValue))
      {
        throw new RuntimeException(site, $"Map key must be 'string', 'int', or an enum type, but got '{key.TypeName}'.");
      }
    }

    private static void EnforceFieldWritable(MemberDecl? member, ClassValue? declaringClass, ALKScriptToken site, ScriptEnvironment environment)
    {
      if (member is not FieldDecl { IsReadonly: true }) return;

      if (!(environment.IsInConstructor && environment.CurrentClass == declaringClass))
      {
        throw new RuntimeException(site, $"Cannot assign to readonly field '{site.Lexeme}' outside of '{declaringClass!.Declaration.Name.Lexeme}'s constructor.");
      }
    }

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

        case MapValue mapValue:
          return GetMapMember(mapValue, name);

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

    private ALKScriptValue GetMapMember(MapValue map, ALKScriptToken name)
    {
      switch (name.Lexeme)
      {
        case "add":
          return new NativeFunctionValue("add", 2, arguments =>
          {
            ValidateMapKey(arguments[0], name);
            if (map.Entries.ContainsKey(arguments[0]))
              throw new RuntimeException(name, "Key already exists in map. Use indexer assignment to update an existing key.");
            map.Entries[arguments[0]] = arguments[1];
            return NullValue.Instance;
          });

        case "remove":
          return new NativeFunctionValue("remove", 1, arguments =>
          {
            ValidateMapKey(arguments[0], name);
            if (!map.Entries.TryGetValue(arguments[0], out var removed))
              throw new RuntimeException(name, "Key not found in map.");
            map.Entries.Remove(arguments[0]);
            return removed;
          });

        case "has":
          return new NativeFunctionValue("has", 1, arguments =>
          {
            ValidateMapKey(arguments[0], name);
            return BoolValue.Of(map.Entries.ContainsKey(arguments[0]));
          });

        case "keys":
          return new NativeFunctionValue("keys", 0, _ =>
          {
            var keys = new System.Collections.Generic.List<ALKScriptValue>(map.Entries.Keys);
            return new ArrayValue(keys);
          });

        case "values":
          return new NativeFunctionValue("values", 0, _ =>
          {
            var values = new System.Collections.Generic.List<ALKScriptValue>(map.Entries.Values);
            return new ArrayValue(values);
          });

        default:
          throw new RuntimeException(name, $"Undefined property '{name.Lexeme}' on 'map'.");
      }
    }

    private ALKScriptValue GetArrayMember(ArrayValue array, ALKScriptToken name)
    {
      switch (name.Lexeme)
      {
        case "length":
          return new IntValue(array.Items.Count);

        case "push":
          return new NativeFunctionValue("push", 1, arguments =>
          {
            if (array.IsFrozen) throw new RuntimeException(name, "Cannot call 'push' on a 'const' array.");
            array.Items.Add(arguments[0]);
            return new IntValue(array.Items.Count);
          });

        case "pop":
          return new NativeFunctionValue("pop", 0, arguments =>
          {
            if (array.IsFrozen) throw new RuntimeException(name, "Cannot call 'pop' on a 'const' array.");
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
            if (array.IsFrozen) throw new RuntimeException(name, "Cannot call 'remove' on a 'const' array.");
            int index = ExpectIndex(arguments[0], name, array.Items.Count, "Array");

            var removed = array.Items[index];
            array.Items.RemoveAt(index);
            return removed;
          });

        case "map":
          return new CursorNativeFunctionValue("map", 1, (arguments, cursor) =>
          {
            var callback = ExpectCallable(arguments[0], name, "map");

            var results = new List<ALKScriptValue>(array.Items.Count);
            foreach (var item in array.Items)
            {
              var mappedStep = cursor.Call(callback, new List<ALKScriptValue> { item }, name);
              if (mappedStep.IsAwaiting) return mappedStep;

              if (cursor.Signal != null)
              {
                return StepResult.Completed(NullValue.Instance);
              }

              results.Add(mappedStep.Value!);
            }

            return StepResult.Completed(new ArrayValue(results));
          });

        case "filter":
          return new CursorNativeFunctionValue("filter", 1, (arguments, cursor) =>
          {
            var callback = ExpectCallable(arguments[0], name, "filter");

            var results = new List<ALKScriptValue>();
            foreach (var item in array.Items)
            {
              var keepStep = cursor.Call(callback, new List<ALKScriptValue> { item }, name);
              if (keepStep.IsAwaiting) return keepStep;

              if (cursor.Signal != null)
              {
                return StepResult.Completed(NullValue.Instance);
              }

              if (!(keepStep.Value is BoolValue boolValue))
              {
                throw new RuntimeException(name, $"'filter' callback must return a 'bool', but got '{keepStep.Value!.TypeName}'.");
              }

              if (boolValue.Value)
              {
                results.Add(item);
              }
            }

            return StepResult.Completed(new ArrayValue(results));
          });

        case "indexOf":
          return new NativeFunctionValue("indexOf", 1, arguments =>
          {
            for (int i = 0; i < array.Items.Count; i++)
            {
              if (Operators.AreEqual(array.Items[i], arguments[0]))
                return new IntValue(i);
            }
            return new IntValue(-1);
          });

        case "includes":
          return new NativeFunctionValue("includes", 1, arguments =>
          {
            foreach (var item in array.Items)
            {
              if (Operators.AreEqual(item, arguments[0]))
                return BoolValue.True;
            }
            return BoolValue.False;
          });

        case "find":
          return new CursorNativeFunctionValue("find", 1, (arguments, cursor) =>
          {
            var callback = ExpectCallable(arguments[0], name, "find");
            foreach (var item in array.Items)
            {
              var step = cursor.Call(callback, new List<ALKScriptValue> { item }, name);
              if (step.IsAwaiting) return step;
              if (cursor.Signal != null) return StepResult.Completed(NullValue.Instance);
              if (step.Value is BoolValue { Value: true })
                return StepResult.Completed(item);
            }
            return StepResult.Completed(NullValue.Instance);
          });

        case "findIndex":
          return new CursorNativeFunctionValue("findIndex", 1, (arguments, cursor) =>
          {
            var callback = ExpectCallable(arguments[0], name, "findIndex");
            for (int i = 0; i < array.Items.Count; i++)
            {
              var step = cursor.Call(callback, new List<ALKScriptValue> { array.Items[i] }, name);
              if (step.IsAwaiting) return step;
              if (cursor.Signal != null) return StepResult.Completed(NullValue.Instance);
              if (step.Value is BoolValue { Value: true })
                return StepResult.Completed(new IntValue(i));
            }
            return StepResult.Completed(new IntValue(-1));
          });

        case "some":
          return new CursorNativeFunctionValue("some", 1, (arguments, cursor) =>
          {
            var callback = ExpectCallable(arguments[0], name, "some");
            foreach (var item in array.Items)
            {
              var step = cursor.Call(callback, new List<ALKScriptValue> { item }, name);
              if (step.IsAwaiting) return step;
              if (cursor.Signal != null) return StepResult.Completed(NullValue.Instance);
              if (!(step.Value is BoolValue b))
                throw new RuntimeException(name, $"'some' callback must return a 'bool', but got '{step.Value!.TypeName}'.");
              if (b.Value) return StepResult.Completed(BoolValue.True);
            }
            return StepResult.Completed(BoolValue.False);
          });

        case "every":
          return new CursorNativeFunctionValue("every", 1, (arguments, cursor) =>
          {
            var callback = ExpectCallable(arguments[0], name, "every");
            foreach (var item in array.Items)
            {
              var step = cursor.Call(callback, new List<ALKScriptValue> { item }, name);
              if (step.IsAwaiting) return step;
              if (cursor.Signal != null) return StepResult.Completed(NullValue.Instance);
              if (!(step.Value is BoolValue b))
                throw new RuntimeException(name, $"'every' callback must return a 'bool', but got '{step.Value!.TypeName}'.");
              if (!b.Value) return StepResult.Completed(BoolValue.False);
            }
            return StepResult.Completed(BoolValue.True);
          });

        case "sort":
          return new CursorNativeFunctionValue("sort", 1, (arguments, cursor) =>
          {
            if (array.IsFrozen) throw new RuntimeException(name, "Cannot call 'sort' on a 'const' array.");
            var comparator = ExpectCallable2(arguments[0], name, "sort");
            var snapshot = new List<ALKScriptValue>(array.Items);
            var exception = (RuntimeException?)null;
            snapshot.Sort((a, b) =>
            {
              if (exception != null) return 0;
              var step = cursor.Call(comparator, new List<ALKScriptValue> { a, b }, name);
              if (!(step.Value is IntValue iv))
              {
                exception = new RuntimeException(name, $"'sort' comparator must return an 'int', but got '{step.Value!.TypeName}'.");
                return 0;
              }
              return iv.Value < 0 ? -1 : iv.Value > 0 ? 1 : 0;
            });
            if (exception != null) throw exception;
            array.Items.Clear();
            array.Items.AddRange(snapshot);
            return StepResult.Completed(NullValue.Instance);
          });

        case "reduce":
          return new CursorNativeFunctionValue("reduce", 2, (arguments, cursor) =>
          {
            var callback = ExpectCallable2(arguments[0], name, "reduce");
            var accumulator = arguments[1];
            foreach (var item in array.Items)
            {
              var step = cursor.Call(callback, new List<ALKScriptValue> { accumulator, item }, name);
              if (step.IsAwaiting) return step;
              if (cursor.Signal != null) return StepResult.Completed(NullValue.Instance);
              accumulator = step.Value!;
            }
            return StepResult.Completed(accumulator);
          });

        case "reverse":
          return new NativeFunctionValue("reverse", 0, _ =>
          {
            if (array.IsFrozen) throw new RuntimeException(name, "Cannot call 'reverse' on a 'const' array.");
            array.Items.Reverse();
            return NullValue.Instance;
          });

        default:
          throw new RuntimeException(name, $"Undefined property '{name.Lexeme}' on '{array.TypeName}'.");
      }
    }

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

        case "padLeft":
          return new NativeFunctionValue("padLeft", 2, arguments =>
          {
            int width = ExpectPositiveInt(arguments[0], name, "padLeft");
            string pad = ExpectString(arguments[1], name, "padLeft");
            if (pad.Length != 1)
              throw new RuntimeException(name, $"'padLeft' padding character must be a single character but got \"{pad}\".");
            return new StringValue(self.PadLeft(width, pad[0]));
          });

        case "padRight":
          return new NativeFunctionValue("padRight", 2, arguments =>
          {
            int width = ExpectPositiveInt(arguments[0], name, "padRight");
            string pad = ExpectString(arguments[1], name, "padRight");
            if (pad.Length != 1)
              throw new RuntimeException(name, $"'padRight' padding character must be a single character but got \"{pad}\".");
            return new StringValue(self.PadRight(width, pad[0]));
          });

        case "repeat":
          return new NativeFunctionValue("repeat", 1, arguments =>
          {
            int count = ExpectNonNegativeInt(arguments[0], name, "repeat");
            if (count == 0) return new StringValue(string.Empty);
            var sb = new System.Text.StringBuilder(self.Length * count);
            for (int i = 0; i < count; i++) sb.Append(self);
            return new StringValue(sb.ToString());
          });

        case "trimStart":
          return new NativeFunctionValue("trimStart", 0, _ => new StringValue(self.TrimStart()));

        case "trimEnd":
          return new NativeFunctionValue("trimEnd", 0, _ => new StringValue(self.TrimEnd()));

        case "charAt":
          return new NativeFunctionValue("charAt", 1, arguments =>
          {
            int index = ExpectNonNegativeInt(arguments[0], name, "charAt");
            if (index >= self.Length)
              throw new RuntimeException(name, $"'charAt' index {index} is out of bounds for a string of length {self.Length}.");
            return new StringValue(self[index].ToString());
          });

        case "toInt":
          return new NativeFunctionValue("toInt", 0, _ =>
          {
            if (!long.TryParse(self, out long intResult))
              throw new RuntimeException(name, $"Cannot convert \"{self}\" to 'int'.");
            return new IntValue(intResult);
          });

        case "toFloat":
          return new NativeFunctionValue("toFloat", 0, _ =>
          {
            if (!double.TryParse(self, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double floatResult))
              throw new RuntimeException(name, $"Cannot convert \"{self}\" to 'float'.");
            return new FloatValue(floatResult);
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

    private static CallableValue ExpectCallable2(ALKScriptValue value, ALKScriptToken site, string memberName)
    {
      if (!(value is CallableValue callable) || callable.Arity != 2)
      {
        throw new RuntimeException(site, $"'{memberName}' expects a callback of arity 2 but got '{value.TypeName}'.");
      }

      return callable;
    }

    private static int ExpectPositiveInt(ALKScriptValue value, ALKScriptToken site, string memberName)
    {
      if (!(value is IntValue intValue) || intValue.Value <= 0)
      {
        throw new RuntimeException(site, $"'{memberName}' expects a positive int argument but got '{value}'.");
      }

      return (int)intValue.Value;
    }

    /// <summary>
    /// Step 6/7 of the cursor-rewrite plan: <c>await</c>. If
    /// <see cref="EvaluationCursor.HasResumeValue"/> is set, this is the exact
    private StepResult EvalTypeof(TypeofExpr expression, ScriptEnvironment environment)
    {
      var step = _cursor.Eval(expression.Operand, environment);
      if (step.IsAwaiting) return step;
      return StepResult.Completed(new StringValue(step.Value!.TypeName));
    }

    /// <c>await</c> a prior <see cref="StepResult.Awaiting"/> suspended on —
    /// substitute the resumed value without re-evaluating
    /// <see cref="AwaitExpr.Operand"/> (which already ran and produced the
    /// thunk before suspension). Otherwise evaluate the operand and either
    /// resolve immediately (already-completed thunk, or a
    /// <see cref="PendingOperationValue"/> that starts synchronously) or — if
    /// <paramref name="allowSuspend"/> — return <see cref="StepResult.Awaiting"/>.
    /// </summary>
    private StepResult EvalAwait(AwaitExpr expression, ScriptEnvironment environment, bool allowSuspend)
    {
      // Only the AwaitExpr at the very leaf of the resume trail — reached once
      // every enclosing resumable construct has finished fast-forwarding into
      // its recorded child (IsResuming has dropped back to false) — is the one
      // that originally suspended. An outer `await <call>` (e.g. `await load()`
      // where load() itself awaits) must NOT consume the resume value; it needs
      // to re-evaluate its operand so the call's body resumes via the trail.
      if (_cursor.HasResumeValue && !_cursor.IsResuming)
      {
        var resumeValue = _cursor.TakeResumeValue();

        var faultMessage = _cursor.TakePendingFault();
        if (faultMessage != null)
        {
          _cursor.Signal = Signal.Thrown(new StringValue(faultMessage));
          return StepResult.Completed(NullValue.Instance);
        }

        return StepResult.Completed(resumeValue);
      }

      if (_cursor.TryTakeResumeComposite(out var compositeElements))
      {
        return ResolveWhenAll(compositeElements, environment, expression.Keyword);
      }

      var operandStep = _cursor.Eval(expression.Operand, environment);
      if (operandStep.IsAwaiting) return operandStep;
      var operand = operandStep.Value!;

      if (_cursor.Signal != null)
      {
        return StepResult.Completed(NullValue.Instance);
      }

      return operand is ArrayValue array
        ? EvalWhenAll(array.Items, environment, expression.Keyword, allowSuspend)
        : AwaitIfNeeded(operand, environment, expression.Keyword, allowSuspend);
    }

    /// <summary>
    /// Resolves <paramref name="value"/> to its settled result if it is a
    /// <see cref="ThunkValue"/> or <see cref="PendingOperationValue"/> (the
    /// shapes a <c>thunk</c>/<c>thunk&lt;T&gt;</c>-typed expression evaluates
    /// to), otherwise returns it unchanged.
    /// </summary>
    private StepResult AwaitIfNeeded(ALKScriptValue value, ScriptEnvironment environment, ALKScriptToken site, bool allowSuspend)
    {
      switch (value)
      {
        case ThunkValue thunkValue:
          ValidateThunkResult(thunkValue.Result, thunkValue.ElementType, environment, site);
          return StepResult.Completed(thunkValue.Result);

        case PendingOperationValue pending:
          return AwaitPending(pending, environment, site, allowSuspend);

        default:
          throw new RuntimeException(site, $"'await' can only be applied to a 'thunk' value, but got '{value.TypeName}'.");
      }
    }

    /// <summary>
    /// Handles <c>await &lt;pendingOperationValue&gt;</c> with record-and-replay
    /// awareness, mirroring the old evaluator's <c>AwaitPending</c>: during
    /// replay (log not yet exhausted), the next log entry is consumed
    /// positionally and its recorded result or fault is returned immediately —
    /// the host-side effect is never started. During live execution,
    /// <see cref="PendingOperationValue.Start"/> is called and the outcome is
    /// recorded to the log (immediately if it settles synchronously, or by
    /// <see cref="EvaluationCursor.Resume"/>/<see cref="EvaluationCursor.ResumeFaulted"/>
    /// if it suspends).
    /// </summary>
    private StepResult AwaitPending(PendingOperationValue pending, ScriptEnvironment environment, ALKScriptToken site, bool allowSuspend)
    {
      var entry = _cursor.TryReplayNext();
      if (entry != null)
      {
        pending.MarkReplayed();

        if (entry.IsFaulted)
        {
          _cursor.Signal = Signal.Thrown(new StringValue(entry.FaultMessage!));
          return StepResult.Completed(NullValue.Instance);
        }

        ValidateThunkResult(entry.Result!, pending.ElementType, environment, site);
        return StepResult.Completed(entry.Result!);
      }

      var status = pending.Start();
      switch (status)
      {
        case OperationStatus.Resolved resolved:
          _cursor.RecordEntry(OperationLogEntry.FromResult(pending.Operation, resolved.Value));
          ValidateThunkResult(resolved.Value, pending.ElementType, environment, site);
          return StepResult.Completed(resolved.Value);

        case OperationStatus.Faulted faulted:
          if (faulted.Error is RuntimeException) throw faulted.Error;

          _cursor.RecordEntry(OperationLogEntry.FromFault(pending.Operation, faulted.Error.Message));
          _cursor.Signal = Signal.Thrown(new StringValue(faulted.Error.Message));
          return StepResult.Completed(NullValue.Instance);

        default: // Pending
          if (!allowSuspend)
          {
            throw new RuntimeException(site, "'await' in this expression position cannot suspend on an unresolved 'thunk' — rewrite as 'var t = await ...;' first.");
          }

          return StepResult.Awaiting(AwaitHandle.ForOperation(pending, pending.ElementType, site));
      }
    }

    /// <summary>
    /// <c>await [a, b, c]</c> — sugar for "whenAll" (docs/ASYNC_AWAIT_DESIGN.md
    /// decision #13). Classifies each element exactly as the old evaluator's
    /// <c>EvalWhenAll</c> did: a <see cref="PendingOperationValue"/> is tried
    /// against the replay log first (consuming the next entry positionally),
    /// otherwise started live; a <see cref="ThunkValue"/> contributes its
    /// already-settled result; anything else is already resolved. If
    /// every element is already settled, resolves synchronously via
    /// <see cref="ResolveWhenAll"/>. Otherwise — if <paramref name="allowSuspend"/> —
    /// returns <see cref="StepResult.Awaiting"/> with a composite
    /// <see cref="AwaitHandle"/> (see <see cref="AwaitHandle.ForComposite"/>);
    /// <see cref="EvalAwait"/>'s <see cref="EvaluationCursor.TryTakeResumeComposite"/>
    /// branch later resolves it via the same <see cref="ResolveWhenAll"/>.
    /// </summary>
    private StepResult EvalWhenAll(IReadOnlyList<ALKScriptValue> items, ScriptEnvironment environment, ALKScriptToken site, bool allowSuspend)
    {
      var elements = new AwaitElement[items.Count];

      for (int i = 0; i < items.Count; i++)
      {
        switch (items[i])
        {
          case PendingOperationValue pending:
            var entry = _cursor.TryReplayNext();
            if (entry != null)
            {
              pending.MarkReplayed();
              elements[i] = entry.IsFaulted
                ? AwaitElement.ForReplayedFault(entry.FaultMessage!, pending.ElementType)
                : AwaitElement.ForResolved(entry.Result!, pending.ElementType);
            }
            else
            {
              pending.Start();
              elements[i] = AwaitElement.ForOperation(pending, pending.ElementType);
            }
            break;

          case ThunkValue thunkValue:
            elements[i] = AwaitElement.ForResolved(thunkValue.Result, thunkValue.ElementType);
            break;

          default:
            elements[i] = AwaitElement.ForResolved(items[i], elementType: null);
            break;
        }
      }

      var needsSuspend = false;
      foreach (var element in elements)
      {
        if (element.NeedsSuspend) { needsSuspend = true; break; }
      }

      if (!needsSuspend)
      {
        return ResolveWhenAll(elements, environment, site);
      }

      if (!allowSuspend)
      {
        throw new RuntimeException(site, "'await' in this expression position cannot suspend on an array with an in-flight operation — rewrite as 'var t = await [...];' first.");
      }

      return StepResult.Awaiting(AwaitHandle.ForComposite(elements, site));
    }

    /// <summary>
    /// Resolves a fully-settled set of <paramref name="elements"/> into the
    /// result of <c>await [a, b, c]</c>: each live element's
    /// <see cref="PendingOperationValue.Status"/> is inspected (guaranteed
    /// settled by this point — either it never needed to suspend, or the
    /// host's "Pump" polled it to settled), with results/faults
    /// recorded to the replay log (and faults reported to the host) in
    /// source order, mirroring the old evaluator's <c>EvalWhenAll</c>. Any
    /// faults are aggregated into a single <see cref="Signal.Thrown"/>
    /// (one message verbatim, or "Multiple operations failed: ..." for more
    /// than one); otherwise each resolved value is validated against its
    /// declared element type and the whole array is returned.
    /// </summary>
    private StepResult ResolveWhenAll(IReadOnlyList<AwaitElement> elements, ScriptEnvironment environment, ALKScriptToken site)
    {
      var count = elements.Count;
      var resolved = new ALKScriptValue?[count];
      var faultMessages = new string?[count];

      for (int i = 0; i < count; i++)
      {
        var element = elements[i];

        if (element.Resolved != null)
        {
          resolved[i] = element.Resolved;
          continue;
        }

        if (element.ReplayedFaultMessage != null)
        {
          faultMessages[i] = element.ReplayedFaultMessage;
          continue;
        }

        var pendingOperation = (PendingOperationValue)element.Source!;
        switch (pendingOperation.Status)
        {
          case OperationStatus.Faulted faulted:
            if (faulted.Error is RuntimeException) throw faulted.Error;

            faultMessages[i] = faulted.Error.Message;
            _cursor.RecordEntry(OperationLogEntry.FromFault(element.Operation!, faulted.Error.Message));
            _functionValueFactory.ReportOperationFaulted(element.Operation!, faulted.Error);
            break;

          case OperationStatus.Resolved settled:
            resolved[i] = settled.Value;
            _cursor.RecordEntry(OperationLogEntry.FromResult(element.Operation!, settled.Value));
            break;
        }
      }

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
        _cursor.Signal = Signal.Thrown(new StringValue(message));
        return StepResult.Completed(NullValue.Instance);
      }

      for (int i = 0; i < count; i++)
      {
        if (resolved[i] != null)
        {
          ValidateThunkResult(resolved[i]!, elements[i].ElementType, environment, site);
        }
      }

      var results = new List<ALKScriptValue>(count);
      for (int i = 0; i < count; i++) results.Add(resolved[i] ?? NullValue.Instance);
      return StepResult.Completed(new ArrayValue(results));
    }

    private static void ValidateThunkResult(ALKScriptValue result, TypeNode? elementType, ScriptEnvironment environment, ALKScriptToken site)
    {
      if (elementType != null && !TypeChecking.MatchesType(result, elementType, environment, site))
      {
        throw new RuntimeException(site, $"Operation declared 'thunk<{elementType}>' resolved to a value of type '{result.TypeName}', expected '{elementType}'.");
      }
    }
  }
}
