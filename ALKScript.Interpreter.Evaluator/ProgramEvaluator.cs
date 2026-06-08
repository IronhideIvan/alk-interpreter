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
  ///
  /// The actual tree-walking is composed from three collaborators —
  /// <see cref="StatementExecutor"/>, <see cref="ExpressionEvaluator"/> and
  /// <see cref="CallInvoker"/> — which call into each other and share the
  /// pending-signal slot used for "return"/"throw" unwinding. This class wires
  /// them together by implementing <see cref="IEvaluationContext"/>, the
  /// interface they recurse through; that indirection is what lets three
  /// mutually-dependent collaborators be constructed without a cycle.
  /// </summary>
  public class ProgramEvaluator : IEvaluator, IEvaluationContext
  {
    private readonly IStatementExecutor _statements;
    private readonly IExpressionEvaluator _expressions;
    private readonly ICallInvoker _calls;

    private Signal? _signal;

    /// <summary>
    /// Creates an evaluator. <paramref name="nativeBindings"/> supplies the host
    /// implementations for <c>native</c> function/method declarations, keyed by
    /// declared name; a <c>native</c> declaration with no matching binding fails
    /// with a <see cref="RuntimeException"/> as soon as it is declared.
    /// </summary>
    public ProgramEvaluator(ScriptNativeBindings? nativeBindings = null)
      : this(new FunctionValueFactory(nativeBindings))
    {
    }

    /// <summary>
    /// Creates an evaluator with an explicit <see cref="IFunctionValueFactory"/>,
    /// e.g. for testing or to supply a host-specific binding strategy.
    /// </summary>
    public ProgramEvaluator(IFunctionValueFactory functionValueFactory)
      : this(functionValueFactory, new EvaluationComponentFactory())
    {
    }

    /// <summary>
    /// Creates an evaluator with explicit <see cref="IFunctionValueFactory"/> and
    /// <see cref="IEvaluationComponentFactory"/> implementations. Internal — the
    /// component factory deals in the internal collaborator interfaces — but
    /// reachable from tests via <c>InternalsVisibleTo</c>.
    /// </summary>
    internal ProgramEvaluator(IFunctionValueFactory functionValueFactory, IEvaluationComponentFactory componentFactory)
    {
      _statements = componentFactory.CreateStatementExecutor(this, functionValueFactory);
      _expressions = componentFactory.CreateExpressionEvaluator(this, functionValueFactory);
      _calls = componentFactory.CreateCallInvoker(this);
    }

    public void Evaluate(ModuleGraph graph)
    {
      var globals = new ScriptEnvironment();

      foreach (var declaration in graph.EntryModule.Program.Declarations)
      {
        _statements.Execute(declaration, globals);

        if (_signal != null)
        {
          break;
        }
      }

      if (_signal is { Kind: SignalKind.Thrown } thrown)
      {
        _signal = null;
        throw new RuntimeException(
          AstTokenLocator.EndOfFile,
          $"Uncaught exception: {Operators.Stringify(thrown.Value)}");
      }

      // A stray top-level "return" simply ends the module's execution.
      _signal = null;
    }

    // ---------------------------------------------------------------------
    // IEvaluationContext — routes recursive calls between the collaborators
    // and exposes the pending-signal slot they coordinate on.
    // ---------------------------------------------------------------------

    Signal? IEvaluationContext.Signal
    {
      get => _signal;
      set => _signal = value;
    }

    void IEvaluationContext.Execute(Stmt statement, ScriptEnvironment environment)
      => _statements.Execute(statement, environment);

    void IEvaluationContext.ExecuteBlock(IReadOnlyList<Stmt> statements, ScriptEnvironment environment)
      => _statements.ExecuteBlock(statements, environment);

    ALKScriptValue IEvaluationContext.Eval(Expr expression, ScriptEnvironment environment)
      => _expressions.Eval(expression, environment);

    ALKScriptValue IEvaluationContext.Call(ALKScriptValue callee, IReadOnlyList<ALKScriptValue> arguments, ALKScriptToken site)
      => _calls.Call(callee, arguments, site);

    ALKScriptValue IEvaluationContext.Construct(ClassValue classValue, IReadOnlyList<ALKScriptValue> arguments, ALKScriptToken site)
      => _calls.Construct(classValue, arguments, site);
  }
}
