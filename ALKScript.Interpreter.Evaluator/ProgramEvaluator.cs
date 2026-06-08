using System.Collections.Generic;
using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Common.Evaluation;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Common.Modules;
using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Evaluator
{
  /// <summary>
  /// Tree-walking evaluator that executes a <see cref="ModuleGraph"/> by
  /// running the entry module's top-level declarations and statements,
  /// producing/consuming <see cref="ALKScriptValue"/>s as it goes.
  /// </summary>
  public class ProgramEvaluator : IEvaluator
  {
    private readonly IReadOnlyDictionary<string, NativeFunctionImplementation> _nativeBindings;

    /// <summary>
    /// A pending non-local exit ("return" or "throw") raised while executing
    /// the statement or evaluating the expression currently in progress.
    /// Checked after each sub-execution/sub-evaluation so control can unwind
    /// to the right handler (a function call, a "try", or the top level)
    /// without using .NET exceptions for script-level control flow — a script
    /// "throw" should not cost (or look like) a .NET exception.
    /// </summary>
    private Signal? _signal;

    /// <summary>
    /// Creates an evaluator. <paramref name="nativeBindings"/> supplies the host
    /// implementations for <c>native</c> function/method declarations, keyed by
    /// declared name; a <c>native</c> declaration with no matching binding fails
    /// with a <see cref="RuntimeException"/> as soon as it is declared.
    /// </summary>
    public ProgramEvaluator(IReadOnlyDictionary<string, NativeFunctionImplementation>? nativeBindings = null)
    {
      _nativeBindings = nativeBindings ?? new Dictionary<string, NativeFunctionImplementation>();
    }

    public void Evaluate(ModuleGraph graph)
    {
      var globals = new Environment();

      ExecuteModule(graph.EntryModule, globals);

      if (_signal is { Kind: SignalKind.Thrown } thrown)
      {
        _signal = null;
        throw new RuntimeException(
          new ALKScriptToken(ALKScriptTokenType.EndOfFile, string.Empty, 0, 0),
          $"Uncaught exception: {Stringify(thrown.Value)}");
      }

      // A stray top-level "return" simply ends the module's execution.
      _signal = null;
    }

    private void ExecuteModule(LoadedModule module, Environment environment)
    {
      foreach (var declaration in module.Program.Declarations)
      {
        Execute(declaration, environment);

        if (_signal != null)
        {
          return;
        }
      }
    }

    // ---------------------------------------------------------------------
    // Statements
    // ---------------------------------------------------------------------

    private void Execute(Stmt statement, Environment environment)
    {
      if (_signal != null)
      {
        return;
      }

      switch (statement)
      {
        case StatementDecl statementDecl:
          Execute(statementDecl.Statement, environment);
          break;

        case VariableDecl variableDecl:
          ExecuteVariableDecl(variableDecl, environment);
          break;

        case FunctionDecl functionDecl:
          environment.Define(functionDecl.Name.Lexeme, MakeFunctionValue(functionDecl, environment));
          break;

        case ClassDecl classDecl:
          ExecuteClassDecl(classDecl, environment);
          break;

        case ExportDecl exportDecl:
          Execute(exportDecl.Declaration, environment);
          break;

        case ExpressionStmt expressionStmt:
          Eval(expressionStmt.Expression, environment);
          break;

        case BlockStmt blockStmt:
          ExecuteBlock(blockStmt.Statements, new Environment(environment));
          break;

        case IfStmt ifStmt:
          ExecuteIf(ifStmt, environment);
          break;

        case WhileStmt whileStmt:
          ExecuteWhile(whileStmt, environment);
          break;

        case ForStmt forStmt:
          ExecuteFor(forStmt, environment);
          break;

        case ReturnStmt returnStmt:
          ExecuteReturn(returnStmt, environment);
          break;

        case ThrowStmt throwStmt:
          ExecuteThrow(throwStmt, environment);
          break;

        case TryStmt tryStmt:
          ExecuteTry(tryStmt, environment);
          break;

        default:
          throw new RuntimeException(
            TokenOf(statement),
            $"Execution of '{statement.GetType().Name}' is not yet supported.");
      }
    }

    private void ExecuteVariableDecl(VariableDecl declaration, Environment environment)
    {
      ALKScriptValue value = NullValue.Instance;

      if (declaration.Initializer != null)
      {
        value = Eval(declaration.Initializer, environment);

        if (_signal != null)
        {
          return;
        }
      }

      environment.Define(declaration.Name.Lexeme, value);
    }

    private void ExecuteClassDecl(ClassDecl declaration, Environment environment)
    {
      ClassValue? superclass = null;

      if (declaration.SuperclassName != null)
      {
        var superclassValue = LookUp(declaration.SuperclassName, environment);
        superclass = superclassValue as ClassValue
          ?? throw new RuntimeException(declaration.SuperclassName, $"'{declaration.SuperclassName.Lexeme}' is not a class.");
      }

      environment.Define(declaration.Name.Lexeme, new ClassValue(declaration, superclass));
    }

    private void ExecuteBlock(IReadOnlyList<Stmt> statements, Environment environment)
    {
      foreach (var statement in statements)
      {
        Execute(statement, environment);

        if (_signal != null)
        {
          return;
        }
      }
    }

    private void ExecuteIf(IfStmt statement, Environment environment)
    {
      var condition = Eval(statement.Condition, environment);

      if (_signal != null)
      {
        return;
      }

      if (condition.IsTruthy)
      {
        Execute(statement.ThenBranch, environment);
      }
      else if (statement.ElseBranch != null)
      {
        Execute(statement.ElseBranch, environment);
      }
    }

    private void ExecuteWhile(WhileStmt statement, Environment environment)
    {
      while (true)
      {
        var condition = Eval(statement.Condition, environment);

        if (_signal != null)
        {
          return;
        }

        if (!condition.IsTruthy)
        {
          return;
        }

        Execute(statement.Body, environment);

        if (_signal != null)
        {
          return;
        }
      }
    }

    private void ExecuteFor(ForStmt statement, Environment environment)
    {
      var loopEnvironment = new Environment(environment);

      if (statement.Initializer != null)
      {
        Execute(statement.Initializer, loopEnvironment);

        if (_signal != null)
        {
          return;
        }
      }

      while (true)
      {
        if (statement.Condition != null)
        {
          var condition = Eval(statement.Condition, loopEnvironment);

          if (_signal != null)
          {
            return;
          }

          if (!condition.IsTruthy)
          {
            return;
          }
        }

        Execute(statement.Body, loopEnvironment);

        if (_signal != null)
        {
          return;
        }

        if (statement.Increment != null)
        {
          Eval(statement.Increment, loopEnvironment);

          if (_signal != null)
          {
            return;
          }
        }
      }
    }

    private void ExecuteReturn(ReturnStmt statement, Environment environment)
    {
      var value = NullValue.Instance as ALKScriptValue;

      if (statement.Value != null)
      {
        value = Eval(statement.Value, environment);

        if (_signal != null)
        {
          return;
        }
      }

      _signal = Signal.Return(value);
    }

    private void ExecuteThrow(ThrowStmt statement, Environment environment)
    {
      var value = Eval(statement.Value, environment);

      if (_signal != null)
      {
        return;
      }

      _signal = Signal.Thrown(value);
    }

    private void ExecuteTry(TryStmt statement, Environment environment)
    {
      ExecuteBlock(statement.TryBlock.Statements, new Environment(environment));

      if (_signal is { Kind: SignalKind.Thrown } thrown)
      {
        _signal = null;

        if (!TryHandle(statement.CatchClauses, thrown.Value, environment) && _signal == null)
        {
          _signal = thrown;
        }
      }

      if (statement.FinallyBlock != null)
      {
        var pending = _signal;
        _signal = null;

        ExecuteBlock(statement.FinallyBlock.Statements, new Environment(environment));

        // A "return"/"throw" raised by the "finally" block overrides whatever
        // was pending beforehand — matching ordinary try/finally semantics.
        if (_signal == null)
        {
          _signal = pending;
        }
      }
    }

    private bool TryHandle(IReadOnlyList<CatchClause> clauses, ALKScriptValue thrown, Environment environment)
    {
      foreach (var clause in clauses)
      {
        var catchEnvironment = new Environment(environment);

        if (clause.ExceptionName != null)
        {
          catchEnvironment.Define(clause.ExceptionName.Lexeme, thrown);
        }

        ExecuteBlock(clause.Body.Statements, catchEnvironment);
        return true;
      }

      return false;
    }

    // ---------------------------------------------------------------------
    // Expressions
    // ---------------------------------------------------------------------

    private ALKScriptValue Eval(Expr expression, Environment environment)
    {
      if (_signal != null)
      {
        return NullValue.Instance;
      }

      switch (expression)
      {
        case LiteralExpr literal:
          return EvalLiteral(literal);

        case IdentifierExpr identifier:
          return LookUp(identifier.Name, environment);

        case ThisExpr thisExpr:
          return LookUp(thisExpr.Keyword, environment);

        case BaseExpr baseExpr:
          return LookUp(baseExpr.Keyword, environment);

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
            TokenOf(expression),
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

    private ALKScriptValue EvalArrayLiteral(ArrayLiteralExpr expression, Environment environment)
    {
      var items = new List<ALKScriptValue>(expression.Elements.Count);

      foreach (var element in expression.Elements)
      {
        items.Add(Eval(element, environment));

        if (_signal != null)
        {
          return NullValue.Instance;
        }
      }

      return new ArrayValue(items);
    }

    private ALKScriptValue EvalAssignment(AssignmentExpr expression, Environment environment)
    {
      var value = Eval(expression.Value, environment);

      if (_signal != null)
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
          if (_signal != null)
          {
            return NullValue.Instance;
          }
          var instance = target as InstanceValue
            ?? throw new RuntimeException(get.Name, $"Cannot set property '{get.Name.Lexeme}' on a value of type '{target.TypeName}'.");
          instance.Fields[get.Name.Lexeme] = value;
          return value;

        case IndexExpr index:
          var indexed = Eval(index.Target, environment);
          if (_signal != null)
          {
            return NullValue.Instance;
          }
          var array = indexed as ArrayValue
            ?? throw new RuntimeException(index.ClosingBracket, $"Cannot index into a value of type '{indexed.TypeName}'.");
          var indexValue = Eval(index.Index, environment);
          if (_signal != null)
          {
            return NullValue.Instance;
          }
          int position = ExpectIndex(indexValue, index.ClosingBracket, array.Items.Count);
          array.Items[position] = value;
          return value;

        default:
          throw new RuntimeException(TokenOf(expression.Target), "Invalid assignment target.");
      }
    }

    private ALKScriptValue EvalBinary(BinaryExpr expression, Environment environment)
    {
      // Short-circuiting logical operators evaluate their right-hand side lazily.
      if (expression.Operator.Type == ALKScriptTokenType.AmpAmp)
      {
        var left = Eval(expression.Left, environment);
        if (_signal != null)
        {
          return NullValue.Instance;
        }
        return left.IsTruthy ? Eval(expression.Right, environment) : left;
      }

      if (expression.Operator.Type == ALKScriptTokenType.PipePipe)
      {
        var left = Eval(expression.Left, environment);
        if (_signal != null)
        {
          return NullValue.Instance;
        }
        return left.IsTruthy ? left : Eval(expression.Right, environment);
      }

      var leftValue = Eval(expression.Left, environment);
      if (_signal != null)
      {
        return NullValue.Instance;
      }

      var rightValue = Eval(expression.Right, environment);
      if (_signal != null)
      {
        return NullValue.Instance;
      }

      var op = expression.Operator;

      switch (op.Type)
      {
        case ALKScriptTokenType.Plus:
          return EvalAddition(leftValue, rightValue, op);
        case ALKScriptTokenType.Minus:
          return EvalArithmetic(leftValue, rightValue, op, (a, b) => a - b, (a, b) => a - b);
        case ALKScriptTokenType.Star:
          return EvalArithmetic(leftValue, rightValue, op, (a, b) => a * b, (a, b) => a * b);
        case ALKScriptTokenType.Slash:
          return EvalArithmetic(leftValue, rightValue, op, (a, b) => a / b, (a, b) => a / b);
        case ALKScriptTokenType.Percent:
          return EvalArithmetic(leftValue, rightValue, op, (a, b) => a % b, (a, b) => a % b);

        case ALKScriptTokenType.Less:
          return BoolValue.Of(Compare(leftValue, rightValue, op) < 0);
        case ALKScriptTokenType.LessEqual:
          return BoolValue.Of(Compare(leftValue, rightValue, op) <= 0);
        case ALKScriptTokenType.Greater:
          return BoolValue.Of(Compare(leftValue, rightValue, op) > 0);
        case ALKScriptTokenType.GreaterEqual:
          return BoolValue.Of(Compare(leftValue, rightValue, op) >= 0);

        case ALKScriptTokenType.EqualEqual:
          return BoolValue.Of(AreEqual(leftValue, rightValue));
        case ALKScriptTokenType.BangEqual:
          return BoolValue.Of(!AreEqual(leftValue, rightValue));

        default:
          throw new RuntimeException(op, $"Unsupported binary operator '{op.Lexeme}'.");
      }
    }

    private static ALKScriptValue EvalAddition(ALKScriptValue left, ALKScriptValue right, ALKScriptToken op)
    {
      // String concatenation when either operand is a string; numeric addition otherwise.
      if (left is StringValue || right is StringValue)
      {
        return new StringValue(Stringify(left) + Stringify(right));
      }

      return EvalArithmetic(left, right, op, (a, b) => a + b, (a, b) => a + b);
    }

    private static ALKScriptValue EvalArithmetic(
      ALKScriptValue left,
      ALKScriptValue right,
      ALKScriptToken op,
      System.Func<long, long, long> onInts,
      System.Func<double, double, double> onFloats)
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

    private static int Compare(ALKScriptValue left, ALKScriptValue right, ALKScriptToken op)
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

    private static bool AreEqual(ALKScriptValue left, ALKScriptValue right)
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

    private static bool TryToNumber(ALKScriptValue value, out double number)
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

    private ALKScriptValue EvalUnary(UnaryExpr expression, Environment environment)
    {
      var operand = Eval(expression.Operand, environment);

      if (_signal != null)
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

    private ALKScriptValue EvalCall(CallExpr expression, Environment environment)
    {
      var callee = Eval(expression.Callee, environment);

      if (_signal != null)
      {
        return NullValue.Instance;
      }

      var arguments = new List<ALKScriptValue>(expression.Arguments.Count);
      foreach (var argument in expression.Arguments)
      {
        arguments.Add(Eval(argument, environment));

        if (_signal != null)
        {
          return NullValue.Instance;
        }
      }

      return Call(callee, arguments, expression.ClosingParen);
    }

    private ALKScriptValue Call(ALKScriptValue callee, IReadOnlyList<ALKScriptValue> arguments, ALKScriptToken site)
    {
      switch (callee)
      {
        case ClassValue classValue:
          return Construct(classValue, arguments, site);

        case CallableValue callable:
          if (arguments.Count != callable.Arity)
          {
            throw new RuntimeException(site, $"Expected {callable.Arity} argument(s) but got {arguments.Count}.");
          }
          return Invoke(callable, arguments);

        default:
          throw new RuntimeException(site, $"Cannot call a value of type '{callee.TypeName}'.");
      }
    }

    private ALKScriptValue Invoke(CallableValue callable, IReadOnlyList<ALKScriptValue> arguments)
    {
      switch (callable)
      {
        case NativeFunctionValue nativeFunction:
          return nativeFunction.Implementation(arguments);

        case FunctionValue function:
          return InvokeFunction(function, arguments);

        default:
          throw new RuntimeException(
            new ALKScriptToken(ALKScriptTokenType.EndOfFile, string.Empty, 0, 0),
            $"Unsupported callable '{callable.TypeName}'.");
      }
    }

    private ALKScriptValue InvokeFunction(FunctionValue function, IReadOnlyList<ALKScriptValue> arguments)
    {
      var callEnvironment = new Environment(function.Closure);

      if (function.BoundInstance != null)
      {
        callEnvironment.Define("this", function.BoundInstance);
      }

      for (int i = 0; i < function.Declaration.Parameters.Count; i++)
      {
        callEnvironment.Define(function.Declaration.Parameters[i].Name, arguments[i]);
      }

      ExecuteBlock(function.Declaration.Body!.Statements, callEnvironment);

      // "return" is consumed here — it unwinds no further than the call that
      // produced it. A "throw" is left pending so it propagates to the caller.
      if (_signal is { Kind: SignalKind.Return } returned)
      {
        _signal = null;
        return returned.Value;
      }

      return NullValue.Instance;
    }

    private ALKScriptValue Construct(ClassValue classValue, IReadOnlyList<ALKScriptValue> arguments, ALKScriptToken site)
    {
      var instance = new InstanceValue(classValue);
      var constructor = FindConstructor(classValue);

      if (constructor != null)
      {
        if (arguments.Count != constructor.Parameters.Count)
        {
          throw new RuntimeException(site, $"Expected {constructor.Parameters.Count} argument(s) but got {arguments.Count}.");
        }

        var constructorEnvironment = new Environment(GetClassEnvironment(classValue));
        constructorEnvironment.Define("this", instance);

        for (int i = 0; i < constructor.Parameters.Count; i++)
        {
          constructorEnvironment.Define(constructor.Parameters[i].Name, arguments[i]);
        }

        ExecuteBlock(constructor.Body.Statements, constructorEnvironment);

        // A bare "return;" inside a constructor simply ends construction early;
        // a "throw" is left pending so it propagates to the caller.
        if (_signal is { Kind: SignalKind.Return })
        {
          _signal = null;
        }
      }
      else if (arguments.Count != 0)
      {
        throw new RuntimeException(site, $"Expected 0 argument(s) but got {arguments.Count}.");
      }

      return instance;
    }

    private static ConstructorDecl? FindConstructor(ClassValue classValue)
    {
      foreach (var member in classValue.Declaration.Members)
      {
        if (member is ConstructorDecl constructor)
        {
          return constructor;
        }
      }

      return null;
    }

    /// <summary>
    /// The environment methods/constructors close over. A full implementation
    /// would capture the environment the class was declared in; until that is
    /// threaded through <see cref="ClassValue"/>, an empty top-level scope stands in.
    /// </summary>
    private Environment GetClassEnvironment(ClassValue classValue) => new Environment();

    private ALKScriptValue EvalGet(GetExpr expression, Environment environment)
    {
      var target = Eval(expression.Target, environment);

      if (_signal != null)
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
            var methodValue = MakeFunctionValue(MethodAsFunctionDecl(method), GetClassEnvironment(instance.Class));
            return methodValue is FunctionValue boundable ? boundable.BindTo(instance) : methodValue;
          }

          throw new RuntimeException(expression.Name, $"Undefined property '{expression.Name.Lexeme}' on '{target.TypeName}'.");

        case ClassValue classValue:
          var staticMember = classValue.FindMember(expression.Name.Lexeme);
          if (staticMember is MethodDecl staticMethod)
          {
            return MakeFunctionValue(MethodAsFunctionDecl(staticMethod), GetClassEnvironment(classValue));
          }

          throw new RuntimeException(expression.Name, $"Undefined static member '{expression.Name.Lexeme}' on '{target.TypeName}'.");

        default:
          throw new RuntimeException(expression.Name, $"Cannot access property '{expression.Name.Lexeme}' on a value of type '{target.TypeName}'.");
      }
    }

    private ALKScriptValue EvalIndex(IndexExpr expression, Environment environment)
    {
      var target = Eval(expression.Target, environment);

      if (_signal != null)
      {
        return NullValue.Instance;
      }

      var array = target as ArrayValue
        ?? throw new RuntimeException(expression.ClosingBracket, $"Cannot index into a value of type '{target.TypeName}'.");

      var indexValue = Eval(expression.Index, environment);

      if (_signal != null)
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

    private ALKScriptValue EvalNew(NewExpr expression, Environment environment)
    {
      var callee = LookUp(expression.TypeName, environment);
      var classValue = callee as ClassValue
        ?? throw new RuntimeException(expression.TypeName, $"'{expression.TypeName.Lexeme}' is not a class.");

      var arguments = new List<ALKScriptValue>(expression.Arguments.Count);
      foreach (var argument in expression.Arguments)
      {
        arguments.Add(Eval(argument, environment));

        if (_signal != null)
        {
          return NullValue.Instance;
        }
      }

      return Construct(classValue, arguments, expression.Keyword);
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private static ALKScriptValue LookUp(ALKScriptToken name, Environment environment)
    {
      if (environment.TryGet(name.Lexeme, out var value))
      {
        return value;
      }

      throw new RuntimeException(name, $"Undefined name '{name.Lexeme}'.");
    }

    private ALKScriptValue MakeFunctionValue(FunctionDecl declaration, Environment closure)
    {
      if (!declaration.IsNative)
      {
        return new FunctionValue(declaration, closure);
      }

      if (_nativeBindings.TryGetValue(declaration.Name.Lexeme, out var implementation))
      {
        return new NativeFunctionValue(declaration.Name.Lexeme, declaration.Parameters.Count, implementation);
      }

      throw new RuntimeException(declaration.Name, $"Native function '{declaration.Name.Lexeme}' has no host implementation registered.");
    }

    /// <summary>
    /// Methods and functions share evaluation logic but not an AST type;
    /// this adapts a <see cref="MethodDecl"/> to the <see cref="FunctionDecl"/>
    /// shape <see cref="FunctionValue"/> expects.
    /// </summary>
    private static FunctionDecl MethodAsFunctionDecl(MethodDecl method)
    {
      return new FunctionDecl(
        method.IsNative,
        method.IsAsync,
        method.TypeParameters,
        method.ReturnType,
        method.Name,
        method.Parameters,
        method.Body);
    }

    private static string Stringify(ALKScriptValue value) => value.ToString() ?? string.Empty;

    private static ALKScriptToken TokenOf(Stmt statement)
    {
      switch (statement)
      {
        case ReturnStmt returnStmt:
          return returnStmt.Keyword;
        case ThrowStmt throwStmt:
          return throwStmt.Keyword;
        case VariableDecl variableDecl:
          return variableDecl.Name;
        case FunctionDecl functionDecl:
          return functionDecl.Name;
        case ClassDecl classDecl:
          return classDecl.Name;
        default:
          return new ALKScriptToken(ALKScriptTokenType.EndOfFile, string.Empty, 0, 0);
      }
    }

    private static ALKScriptToken TokenOf(Expr expression)
    {
      switch (expression)
      {
        case LiteralExpr literal:
          return literal.Token;
        case IdentifierExpr identifier:
          return identifier.Name;
        case ThisExpr thisExpr:
          return thisExpr.Keyword;
        case BaseExpr baseExpr:
          return baseExpr.Keyword;
        case AssignmentExpr assignment:
          return TokenOf(assignment.Target);
        case BinaryExpr binary:
          return binary.Operator;
        case UnaryExpr unary:
          return unary.Operator;
        case CallExpr call:
          return call.ClosingParen;
        case GetExpr get:
          return get.Name;
        case IndexExpr index:
          return index.ClosingBracket;
        case NewExpr newExpr:
          return newExpr.Keyword;
        case AwaitExpr awaitExpr:
          return awaitExpr.Keyword;
        default:
          return new ALKScriptToken(ALKScriptTokenType.EndOfFile, string.Empty, 0, 0);
      }
    }
  }
}
