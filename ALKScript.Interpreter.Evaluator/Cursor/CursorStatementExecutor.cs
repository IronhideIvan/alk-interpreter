using System.Collections.Generic;
using System.Linq;
using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Common.Evaluation;
using ALKScript.Interpreter.Common.Evaluation.Values;

namespace ALKScript.Interpreter.Evaluator.Cursor
{
  /// <summary>
  /// Cursor-evaluator counterpart to <see cref="StatementExecutor"/>, covering
  /// every statement node type plus the Step 6 suspend/resume mechanism (docs:
  /// validated-nibbling-narwhal plan §3/§4).
  ///
  /// Every resumable construct here (<see cref="ExecuteBlock"/>, <c>if</c>,
  /// loops, <c>switch</c>, <c>try</c>) follows the same pattern: in "normal"
  /// mode it executes as before, but if the child it's executing returns
  /// <see cref="StepResult.Awaiting"/>, it records — via
  /// <see cref="EvaluationCursor.RecordSuspend"/> — which child (by index) and
  /// with which <see cref="ScriptEnvironment"/>, then propagates the awaiting
  /// result upward (every ancestor does the same, building a leaf-to-root
  /// "resume trail"). In "resuming" mode (<see cref="EvaluationCursor.IsResuming"/>),
  /// it instead pops its trail entry via <see cref="EvaluationCursor.PopResumeEntry"/>
  /// and fast-forwards directly into that child/environment, then — once that
  /// completes — falls through into the same "continue iterating" code as
  /// normal mode.
  /// </summary>
  internal sealed class CursorStatementExecutor
  {
    private readonly EvaluationCursor _cursor;

    public CursorStatementExecutor(EvaluationCursor cursor)
    {
      _cursor = cursor;
    }

    public StepResult Execute(Stmt statement, ScriptEnvironment environment)
    {
      switch (statement)
      {
        case StatementDecl statementDecl:
          return Execute(statementDecl.Statement, environment);

        case ExpressionStmt expressionStmt:
          return ExecuteExpressionStmt(expressionStmt, environment);

        case VariableDecl variableDecl:
          return ExecuteVariableDecl(variableDecl, environment);

        case FunctionDecl functionDecl:
          if (environment.IsDefinedLocally(functionDecl.Name.Lexeme))
          {
            throw new RuntimeException(functionDecl.Name, $"A declaration named '{functionDecl.Name.Lexeme}' is already defined in this scope.");
          }
          environment.Define(functionDecl.Name.Lexeme, _cursor.FunctionValueFactory.Create(functionDecl, environment));
          return StepResult.Completed(NullValue.Instance);

        case ClassDecl classDecl:
          return ExecuteClassDecl(classDecl, environment);

        case InterfaceDecl interfaceDecl:
          ExecuteInterfaceDecl(interfaceDecl, environment);
          return StepResult.Completed(NullValue.Instance);

        case EnumDecl enumDecl:
          ExecuteEnumDecl(enumDecl, environment);
          return StepResult.Completed(NullValue.Instance);

        case ExportDecl exportDecl:
          return Execute(exportDecl.Declaration, environment);

        case ReExportDecl:
          // Bindings are wired up by CursorProgramEvaluator.BindModuleDependencies before the declarations loop runs; nothing left to do here.
          return StepResult.Completed(NullValue.Instance);

        case BlockStmt blockStmt:
          return ExecuteBlock(blockStmt.Statements, new ScriptEnvironment(environment));

        case IfStmt ifStmt:
          return ExecuteIf(ifStmt, environment);

        case WhileStmt whileStmt:
          return ExecuteWhile(whileStmt, environment);

        case ForStmt forStmt:
          return ExecuteFor(forStmt, environment);

        case ForeachStmt foreachStmt:
          return ExecuteForeach(foreachStmt, environment);

        case DoWhileStmt doWhileStmt:
          return ExecuteDoWhile(doWhileStmt, environment);

        case SwitchStmt switchStmt:
          return ExecuteSwitch(switchStmt, environment);

        case BreakStmt:
          _cursor.Signal = Signal.Break();
          return StepResult.Completed(NullValue.Instance);

        case ContinueStmt:
          _cursor.Signal = Signal.Continue();
          return StepResult.Completed(NullValue.Instance);

        case ReturnStmt returnStmt:
          return ExecuteReturn(returnStmt, environment);

        case ThrowStmt throwStmt:
          return ExecuteThrow(throwStmt, environment);

        case TryStmt tryStmt:
          return ExecuteTry(tryStmt, environment);

        default:
          throw new RuntimeException(
            AstTokenLocator.Of(statement),
            $"Execution of '{statement.GetType().Name}' is not yet supported by the cursor evaluator.");
      }
    }

    /// <summary>
    /// Executes <paramref name="statements"/> in order against <paramref name="environment"/>,
    /// stopping early on suspension or a pending <see cref="Signal"/>. If
    /// <see cref="EvaluationCursor.IsResuming"/>, fast-forwards directly into
    /// the statement (and environment) recorded when this block last
    /// suspended, then continues with the following statements as normal.
    /// </summary>
    public StepResult ExecuteBlock(IReadOnlyList<Stmt> statements, ScriptEnvironment environment)
    {
      int startIndex = 0;

      if (_cursor.IsResuming)
      {
        var entry = _cursor.PopResumeEntry();
        environment = entry.Environment;

        var resumedStep = Execute(statements[entry.Index], environment);
        if (resumedStep.IsAwaiting)
        {
          _cursor.RecordSuspend(entry.Index, environment);
          return resumedStep;
        }

        if (_cursor.Signal != null)
        {
          return StepResult.Completed(NullValue.Instance);
        }

        startIndex = entry.Index + 1;
      }

      for (int i = startIndex; i < statements.Count; i++)
      {
        var step = Execute(statements[i], environment);
        if (step.IsAwaiting)
        {
          _cursor.RecordSuspend(i, environment);
          return step;
        }

        if (_cursor.Signal != null)
        {
          return StepResult.Completed(NullValue.Instance);
        }
      }

      return StepResult.Completed(NullValue.Instance);
    }

    private StepResult ExecuteExpressionStmt(ExpressionStmt statement, ScriptEnvironment environment)
    {
      var step = _cursor.Eval(statement.Expression, environment, allowSuspend: true);
      if (step.IsAwaiting) return step;

      return StepResult.Completed(NullValue.Instance);
    }

    private StepResult ExecuteVariableDecl(VariableDecl declaration, ScriptEnvironment environment)
    {
      ALKScriptValue value = NullValue.Instance;

      if (declaration.Initializer != null)
      {
        var step = _cursor.Eval(declaration.Initializer, environment, allowSuspend: true);
        if (step.IsAwaiting) return step;
        value = step.Value!;

        if (_cursor.Signal != null)
        {
          return StepResult.Completed(NullValue.Instance);
        }

        TypeChecking.EnsureAssignable(declaration.Type, value, declaration.Name, $"variable '{declaration.Name.Lexeme}'", environment);
      }

      environment.Define(declaration.Name.Lexeme, value, declaration.Type, declaration.IsConst);
      return StepResult.Completed(NullValue.Instance);
    }

    private StepResult ExecuteClassDecl(ClassDecl declaration, ScriptEnvironment environment)
    {
      ClassValue? superclass = null;

      if (declaration.SuperclassName != null)
      {
        var superclassValue = Names.LookUp(declaration.SuperclassName, environment);
        superclass = superclassValue as ClassValue
          ?? throw new RuntimeException(declaration.SuperclassName, $"'{declaration.SuperclassName.Lexeme}' is not a class.");

        if (superclass.Declaration.IsSealed)
        {
          throw new RuntimeException(declaration.SuperclassName, $"Class '{declaration.Name.Lexeme}' cannot extend '{superclass.Declaration.Name.Lexeme}' because it is declared 'sealed'.");
        }
      }

      var interfaces = new List<InterfaceValue>();

      foreach (var interfaceName in declaration.Interfaces)
      {
        var interfaceValue = Names.LookUp(interfaceName, environment);

        if (!(interfaceValue is InterfaceValue resolved))
        {
          throw new RuntimeException(interfaceName, $"'{interfaceName.Lexeme}' is not an interface.");
        }

        interfaces.Add(resolved);
      }

      var classValue = new ClassValue(declaration, superclass, environment, interfaces);

      foreach (var interfaceValue in interfaces)
      {
        foreach (var requiredMethod in interfaceValue.AllMethods())
        {
          var member = classValue.FindMember(requiredMethod.Name.Lexeme);

          if (!(member is MethodDecl implementingMethod) || implementingMethod.Parameters.Count != requiredMethod.Parameters.Count)
          {
            throw new RuntimeException(declaration.Name, $"Class '{declaration.Name.Lexeme}' does not implement method '{requiredMethod.Name.Lexeme}' ({requiredMethod.Parameters.Count} parameter(s)) required by interface '{interfaceValue.Declaration.Name.Lexeme}'.");
          }
        }
      }

      if (environment.IsDefinedLocally(declaration.Name.Lexeme))
      {
        throw new RuntimeException(declaration.Name, $"A declaration named '{declaration.Name.Lexeme}' is already defined in this scope.");
      }

      environment.Define(declaration.Name.Lexeme, classValue);

      InitializeStaticFields(classValue, environment);
      return StepResult.Completed(NullValue.Instance);
    }

    /// <summary>
    /// Evaluates each "static" <see cref="FieldDecl"/> declared directly on
    /// <paramref name="classValue"/> and seeds its initial value. Static field
    /// initializers run once, when the class declaration itself is executed —
    /// not per "new" — and, like other declaration-position evaluation, may
    /// not suspend on an unresolved <c>thunk</c>.
    /// </summary>
    private void InitializeStaticFields(ClassValue classValue, ScriptEnvironment environment)
    {
      var initEnvironment = new ScriptEnvironment(environment) { CurrentClass = classValue };

      foreach (var member in classValue.Declaration.Members)
      {
        if (member is FieldDecl field && field.IsStatic)
        {
          ALKScriptValue fieldValue;

          if (field.Initializer != null)
          {
            var step = _cursor.Eval(field.Initializer, initEnvironment);
            if (step.IsAwaiting)
            {
              throw new RuntimeException(field.Name, "'await' suspending inside a static field initializer is not yet supported by the cursor evaluator.");
            }

            fieldValue = step.Value!;
            if (_cursor.Signal != null) return;

            TypeChecking.EnsureAssignable(field.Type, fieldValue, field.Name, $"static field '{field.Name.Lexeme}'", initEnvironment);
          }
          else
          {
            fieldValue = NullValue.Instance;
          }

          classValue.StaticFields[field.Name.Lexeme] = fieldValue;
        }
      }
    }

    private void ExecuteInterfaceDecl(InterfaceDecl declaration, ScriptEnvironment environment)
    {
      var extends = new List<InterfaceValue>();

      foreach (var extendedName in declaration.Extends)
      {
        var extendedValue = Names.LookUp(extendedName, environment);

        if (!(extendedValue is InterfaceValue resolved))
        {
          throw new RuntimeException(extendedName, $"'{extendedName.Lexeme}' is not an interface.");
        }

        extends.Add(resolved);
      }

      if (environment.IsDefinedLocally(declaration.Name.Lexeme))
      {
        throw new RuntimeException(declaration.Name, $"A declaration named '{declaration.Name.Lexeme}' is already defined in this scope.");
      }

      environment.Define(declaration.Name.Lexeme, new InterfaceValue(declaration, extends));
    }

    private void ExecuteEnumDecl(EnumDecl declaration, ScriptEnvironment environment)
    {
      var members = new Dictionary<string, EnumValue>();
      long nextValue = 0;

      foreach (var member in declaration.Members)
      {
        long value = member.ExplicitValue ?? nextValue;

        if (members.Values.Any(existing => existing.Value == value))
        {
          throw new RuntimeException(member.Name, $"Enum '{declaration.Name.Lexeme}' has more than one member with value {value}.");
        }

        members[member.Name.Lexeme] = new EnumValue(declaration.Name.Lexeme, member.Name.Lexeme, value);
        nextValue = value + 1;
      }

      if (environment.IsDefinedLocally(declaration.Name.Lexeme))
      {
        throw new RuntimeException(declaration.Name, $"A declaration named '{declaration.Name.Lexeme}' is already defined in this scope.");
      }

      environment.Define(declaration.Name.Lexeme, new EnumTypeValue(declaration, members));
    }

    private StepResult ExecuteIf(IfStmt statement, ScriptEnvironment environment)
    {
      if (_cursor.IsResuming)
      {
        var entry = _cursor.PopResumeEntry();
        var resumedBranch = entry.Index == 0 ? statement.ThenBranch : statement.ElseBranch!;

        var resumedStep = Execute(resumedBranch, entry.Environment);
        if (resumedStep.IsAwaiting)
        {
          _cursor.RecordSuspend(entry.Index, entry.Environment);
          return resumedStep;
        }

        return resumedStep;
      }

      var conditionStep = _cursor.Eval(statement.Condition, environment);
      if (conditionStep.IsAwaiting) return conditionStep;
      var condition = conditionStep.Value!;

      if (_cursor.Signal != null)
      {
        return StepResult.Completed(NullValue.Instance);
      }

      if (condition.IsTruthy)
      {
        var step = Execute(statement.ThenBranch, environment);
        if (step.IsAwaiting)
        {
          _cursor.RecordSuspend(0, environment);
          return step;
        }

        return step;
      }

      if (statement.ElseBranch != null)
      {
        var step = Execute(statement.ElseBranch, environment);
        if (step.IsAwaiting)
        {
          _cursor.RecordSuspend(1, environment);
          return step;
        }

        return step;
      }

      return StepResult.Completed(NullValue.Instance);
    }

    private StepResult ExecuteWhile(WhileStmt statement, ScriptEnvironment environment)
    {
      bool resumingBody = false;
      ScriptEnvironment bodyEnvironment = environment;

      if (_cursor.IsResuming)
      {
        var entry = _cursor.PopResumeEntry();
        bodyEnvironment = entry.Environment;
        resumingBody = true;
      }

      while (true)
      {
        if (!resumingBody)
        {
          var conditionStep = _cursor.Eval(statement.Condition, environment);
          if (conditionStep.IsAwaiting) return conditionStep;
          var condition = conditionStep.Value!;

          if (_cursor.Signal != null)
          {
            return StepResult.Completed(NullValue.Instance);
          }

          if (!condition.IsTruthy)
          {
            return StepResult.Completed(NullValue.Instance);
          }

          bodyEnvironment = environment;
        }

        resumingBody = false;

        var bodyStep = Execute(statement.Body, bodyEnvironment);
        if (bodyStep.IsAwaiting)
        {
          _cursor.RecordSuspend(0, bodyEnvironment);
          return bodyStep;
        }

        if (_cursor.Signal != null)
        {
          if (_cursor.Signal.Value.Kind == SignalKind.Break)
          {
            _cursor.Signal = null;
            return StepResult.Completed(NullValue.Instance);
          }

          if (_cursor.Signal.Value.Kind == SignalKind.Continue)
          {
            _cursor.Signal = null;
            continue;
          }

          return StepResult.Completed(NullValue.Instance);
        }
      }
    }

    private StepResult ExecuteFor(ForStmt statement, ScriptEnvironment environment)
    {
      ScriptEnvironment loopEnvironment;
      bool resumingBody = false;

      if (_cursor.IsResuming)
      {
        var entry = _cursor.PopResumeEntry();
        loopEnvironment = entry.Environment;
        resumingBody = true;
      }
      else
      {
        loopEnvironment = new ScriptEnvironment(environment);

        if (statement.Initializer != null)
        {
          var initStep = Execute(statement.Initializer, loopEnvironment);
          if (initStep.IsAwaiting)
          {
            throw new RuntimeException(AstTokenLocator.Of(statement.Initializer), "'await' is not supported in a 'for' loop initializer.");
          }

          if (_cursor.Signal != null)
          {
            return StepResult.Completed(NullValue.Instance);
          }
        }
      }

      while (true)
      {
        if (!resumingBody)
        {
          if (statement.Condition != null)
          {
            var conditionStep = _cursor.Eval(statement.Condition, loopEnvironment);
            if (conditionStep.IsAwaiting) return conditionStep;
            var condition = conditionStep.Value!;

            if (_cursor.Signal != null)
            {
              return StepResult.Completed(NullValue.Instance);
            }

            if (!condition.IsTruthy)
            {
              return StepResult.Completed(NullValue.Instance);
            }
          }
        }

        resumingBody = false;

        var bodyStep = Execute(statement.Body, loopEnvironment);
        if (bodyStep.IsAwaiting)
        {
          _cursor.RecordSuspend(0, loopEnvironment);
          return bodyStep;
        }

        if (_cursor.Signal != null)
        {
          if (_cursor.Signal.Value.Kind == SignalKind.Break)
          {
            _cursor.Signal = null;
            return StepResult.Completed(NullValue.Instance);
          }

          if (_cursor.Signal.Value.Kind != SignalKind.Continue)
          {
            return StepResult.Completed(NullValue.Instance);
          }

          _cursor.Signal = null; // continue — fall through to increment
        }

        if (statement.Increment != null)
        {
          var incrementStep = _cursor.Eval(statement.Increment, loopEnvironment);
          if (incrementStep.IsAwaiting) return incrementStep;

          if (_cursor.Signal != null)
          {
            return StepResult.Completed(NullValue.Instance);
          }
        }
      }
    }

    private StepResult ExecuteForeach(ForeachStmt statement, ScriptEnvironment environment)
    {
      int startIndex;
      ArrayValue array;
      bool resumingBody = false;
      ScriptEnvironment? resumeEnvironment = null;

      if (_cursor.IsResuming)
      {
        var entry = _cursor.PopResumeEntry();
        resumeEnvironment = entry.Environment;
        startIndex = entry.Index;
        resumingBody = true;

        // The collection is re-evaluated on resume (its expression cannot
        // itself suspend, per plan §4) — a known limitation if it has
        // observable side effects.
        var collectionStep = _cursor.Eval(statement.Collection, environment);
        array = collectionStep.Value! as ArrayValue
          ?? throw new RuntimeException(statement.Keyword, $"'foreach' requires an array but got '{collectionStep.Value!.TypeName}'.");
      }
      else
      {
        var collectionStep = _cursor.Eval(statement.Collection, environment);
        if (collectionStep.IsAwaiting) return collectionStep;
        var collectionValue = collectionStep.Value!;

        if (_cursor.Signal != null)
        {
          return StepResult.Completed(NullValue.Instance);
        }

        array = collectionValue as ArrayValue
          ?? throw new RuntimeException(statement.Keyword, $"'foreach' requires an array but got '{collectionValue.TypeName}'.");
        startIndex = 0;
      }

      for (int i = startIndex; i < array.Items.Count; i++)
      {
        ScriptEnvironment loopEnvironment;

        if (resumingBody)
        {
          loopEnvironment = resumeEnvironment!;
          resumingBody = false;
        }
        else
        {
          loopEnvironment = new ScriptEnvironment(environment);
          loopEnvironment.Define(statement.Variable.Lexeme, array.Items[i]);
        }

        var bodyStep = Execute(statement.Body, loopEnvironment);
        if (bodyStep.IsAwaiting)
        {
          _cursor.RecordSuspend(i, loopEnvironment);
          return bodyStep;
        }

        if (_cursor.Signal != null)
        {
          if (_cursor.Signal.Value.Kind == SignalKind.Break)
          {
            _cursor.Signal = null;
            return StepResult.Completed(NullValue.Instance);
          }

          if (_cursor.Signal.Value.Kind == SignalKind.Continue)
          {
            _cursor.Signal = null;
            continue;
          }

          return StepResult.Completed(NullValue.Instance);
        }
      }

      return StepResult.Completed(NullValue.Instance);
    }

    private StepResult ExecuteDoWhile(DoWhileStmt statement, ScriptEnvironment environment)
    {
      bool resumingBody = false;
      ScriptEnvironment bodyEnvironment = environment;

      if (_cursor.IsResuming)
      {
        var entry = _cursor.PopResumeEntry();
        bodyEnvironment = entry.Environment;
        resumingBody = true;
      }

      while (true)
      {
        if (!resumingBody)
        {
          bodyEnvironment = environment;
        }

        resumingBody = false;

        var bodyStep = Execute(statement.Body, bodyEnvironment);
        if (bodyStep.IsAwaiting)
        {
          _cursor.RecordSuspend(0, bodyEnvironment);
          return bodyStep;
        }

        if (_cursor.Signal != null)
        {
          if (_cursor.Signal.Value.Kind == SignalKind.Break)
          {
            _cursor.Signal = null;
            return StepResult.Completed(NullValue.Instance);
          }

          if (_cursor.Signal.Value.Kind == SignalKind.Continue)
          {
            _cursor.Signal = null;
            // fall through to condition check
          }
          else
          {
            return StepResult.Completed(NullValue.Instance);
          }
        }

        var conditionStep = _cursor.Eval(statement.Condition, environment);
        if (conditionStep.IsAwaiting) return conditionStep;
        var condition = conditionStep.Value!;

        if (_cursor.Signal != null)
        {
          return StepResult.Completed(NullValue.Instance);
        }

        if (!condition.IsTruthy)
        {
          return StepResult.Completed(NullValue.Instance);
        }
      }
    }

    private StepResult ExecuteReturn(ReturnStmt statement, ScriptEnvironment environment)
    {
      ALKScriptValue value = NullValue.Instance;

      if (statement.Value != null)
      {
        var step = _cursor.Eval(statement.Value, environment, allowSuspend: true);
        if (step.IsAwaiting) return step;
        value = step.Value!;

        if (_cursor.Signal != null)
        {
          return StepResult.Completed(NullValue.Instance);
        }

        var returnType = environment.CurrentFunctionReturnType;
        if (returnType != null && returnType.Name != "void")
        {
          TypeChecking.EnsureAssignable(returnType, value, statement.Keyword, "the return value", environment);
        }
      }

      _cursor.Signal = Signal.Return(value);
      return StepResult.Completed(NullValue.Instance);
    }

    private StepResult ExecuteThrow(ThrowStmt statement, ScriptEnvironment environment)
    {
      var step = _cursor.Eval(statement.Value, environment, allowSuspend: true);
      if (step.IsAwaiting) return step;
      var value = step.Value!;

      if (_cursor.Signal != null)
      {
        return StepResult.Completed(NullValue.Instance);
      }

      _cursor.Signal = Signal.Thrown(value);
      return StepResult.Completed(NullValue.Instance);
    }

    /// <summary>
    /// Region index used in <see cref="TrailEntry.Index"/> for a suspended
    /// <c>try</c>/<c>catch</c>/<c>finally</c>: <c>0</c> = the <c>try</c> block,
    /// <c>1..CatchClauses.Count</c> = the corresponding <c>catch</c> clause
    /// (1-based), <c>CatchClauses.Count + 1</c> = the <c>finally</c> block.
    /// </summary>
    private StepResult ExecuteTry(TryStmt statement, ScriptEnvironment environment)
    {
      if (_cursor.IsResuming)
      {
        var entry = _cursor.PopResumeEntry();

        if (entry.Index == 0)
        {
          var tryStep = ExecuteBlock(statement.TryBlock.Statements, entry.Environment);
          if (tryStep.IsAwaiting)
          {
            _cursor.RecordSuspend(0, entry.Environment);
            return tryStep;
          }

          return AfterTryBlock(statement, environment);
        }

        if (entry.Index <= statement.CatchClauses.Count)
        {
          var catchStep = ExecuteBlock(statement.CatchClauses[entry.Index - 1].Body.Statements, entry.Environment);
          if (catchStep.IsAwaiting)
          {
            _cursor.RecordSuspend(entry.Index, entry.Environment);
            return catchStep;
          }

          return RunFinally(statement, environment);
        }

        // Finally.
        var pending = entry.PendingSignal;
        var finallyStep = ExecuteBlock(statement.FinallyBlock!.Statements, entry.Environment);
        if (finallyStep.IsAwaiting)
        {
          _cursor.RecordSuspend(entry.Index, entry.Environment, pending);
          return finallyStep;
        }

        return AfterFinally(pending);
      }

      var tryEnvironment = new ScriptEnvironment(environment);
      var newTryStep = ExecuteBlock(statement.TryBlock.Statements, tryEnvironment);
      if (newTryStep.IsAwaiting)
      {
        _cursor.RecordSuspend(0, tryEnvironment);
        return newTryStep;
      }

      return AfterTryBlock(statement, environment);
    }

    private StepResult AfterTryBlock(TryStmt statement, ScriptEnvironment environment)
    {
      if (_cursor.Signal is { Kind: SignalKind.Thrown } thrown)
      {
        _cursor.Signal = null;

        if (statement.CatchClauses.Count > 0)
        {
          var clause = statement.CatchClauses[0];
          var catchEnvironment = new ScriptEnvironment(environment);

          if (clause.ExceptionName != null)
          {
            catchEnvironment.Define(clause.ExceptionName.Lexeme, thrown.Value);
          }

          var catchStep = ExecuteBlock(clause.Body.Statements, catchEnvironment);
          if (catchStep.IsAwaiting)
          {
            _cursor.RecordSuspend(1, catchEnvironment);
            return catchStep;
          }
        }
        else if (_cursor.Signal == null)
        {
          _cursor.Signal = thrown;
        }
      }

      return RunFinally(statement, environment);
    }

    private StepResult RunFinally(TryStmt statement, ScriptEnvironment environment)
    {
      if (statement.FinallyBlock == null)
      {
        return StepResult.Completed(NullValue.Instance);
      }

      var pending = _cursor.Signal;
      _cursor.Signal = null;

      var finallyEnvironment = new ScriptEnvironment(environment);
      var finallyStep = ExecuteBlock(statement.FinallyBlock.Statements, finallyEnvironment);
      if (finallyStep.IsAwaiting)
      {
        _cursor.RecordSuspend(statement.CatchClauses.Count + 1, finallyEnvironment, pending);
        return finallyStep;
      }

      return AfterFinally(pending);
    }

    private StepResult AfterFinally(Signal? pending)
    {
      // A "return"/"throw" raised by the "finally" block overrides whatever
      // was pending beforehand — matching ordinary try/finally semantics.
      if (_cursor.Signal == null)
      {
        _cursor.Signal = pending;
      }

      return StepResult.Completed(NullValue.Instance);
    }

    private StepResult ExecuteSwitch(SwitchStmt statement, ScriptEnvironment environment)
    {
      int startCaseIndex;
      ScriptEnvironment switchEnvironment;

      if (_cursor.IsResuming)
      {
        var entry = _cursor.PopResumeEntry();
        switchEnvironment = entry.Environment;

        var resumedStep = ExecuteBlock(statement.Cases[entry.Index].Body, switchEnvironment);
        if (resumedStep.IsAwaiting)
        {
          _cursor.RecordSuspend(entry.Index, switchEnvironment);
          return resumedStep;
        }

        if (_cursor.Signal != null)
        {
          if (_cursor.Signal.Value.Kind == SignalKind.Break)
          {
            _cursor.Signal = null;
          }

          return StepResult.Completed(NullValue.Instance);
        }

        startCaseIndex = entry.Index + 1;
      }
      else
      {
        var discriminantStep = _cursor.Eval(statement.Discriminant, environment);
        if (discriminantStep.IsAwaiting) return discriminantStep;
        var discriminant = discriminantStep.Value!;

        if (_cursor.Signal != null)
        {
          return StepResult.Completed(NullValue.Instance);
        }

        switchEnvironment = new ScriptEnvironment(environment);

        // Find the first matching 'case', falling back to 'default' if present.
        int matchIndex = -1;
        int defaultIndex = -1;

        for (int i = 0; i < statement.Cases.Count; i++)
        {
          var switchCase = statement.Cases[i];

          if (switchCase.Test == null)
          {
            defaultIndex = i;
            continue;
          }

          var caseValueStep = _cursor.Eval(switchCase.Test, switchEnvironment);
          if (caseValueStep.IsAwaiting) return caseValueStep;
          var caseValue = caseValueStep.Value!;

          if (_cursor.Signal != null)
          {
            return StepResult.Completed(NullValue.Instance);
          }

          if (Operators.AreEqual(discriminant, caseValue))
          {
            matchIndex = i;
            break;
          }
        }

        if (matchIndex == -1)
        {
          matchIndex = defaultIndex;
        }

        if (matchIndex == -1)
        {
          return StepResult.Completed(NullValue.Instance);
        }

        startCaseIndex = matchIndex;
      }

      // Execution falls through subsequent cases until 'break' (or the switch ends).
      for (int i = startCaseIndex; i < statement.Cases.Count; i++)
      {
        var caseStep = ExecuteBlock(statement.Cases[i].Body, switchEnvironment);
        if (caseStep.IsAwaiting)
        {
          _cursor.RecordSuspend(i, switchEnvironment);
          return caseStep;
        }

        if (_cursor.Signal != null)
        {
          if (_cursor.Signal.Value.Kind == SignalKind.Break)
          {
            _cursor.Signal = null;
          }

          return StepResult.Completed(NullValue.Instance);
        }
      }

      return StepResult.Completed(NullValue.Instance);
    }
  }
}
