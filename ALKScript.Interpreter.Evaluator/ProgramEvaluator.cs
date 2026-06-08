using System.Collections.Generic;
using ALKScript.Interpreter.Common;
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
    private readonly IScriptLexer _lexer;
    private readonly IScriptParser _parser;

    private ProgramNode? _globalsProgram;

    private Signal? _signal;

    /// <summary>
    /// Creates an evaluator.
    ///
    /// <paramref name="lexer"/>/<paramref name="parser"/> are injected rather
    /// than constructed here so this project never references the concrete
    /// <c>ALKScriptLexer</c>/<c>ALKScriptParser</c> types (which live in the
    /// Lexer/Parser projects) — only the <see cref="IScriptLexer"/>/
    /// <see cref="IScriptParser"/> interfaces from Common. The evaluator uses
    /// them solely to compile the reserved "globals.alk" prelude (see
    /// <see cref="GlobalsSource"/>) through the exact same lex -&gt; parse
    /// pipeline as any other ALKScript source.
    ///
    /// <paramref name="nativeBindings"/> supplies the host implementations for
    /// <c>native</c> function/method declarations, keyed by declared name —
    /// both the entry module's own <c>native</c> declarations and the ones in
    /// the prelude resolve against this table. A <c>native</c> declaration
    /// with no matching binding fails with a <see cref="RuntimeException"/> as
    /// soon as it is declared — which would otherwise force every host to
    /// register a binding for every prelude global whether or not its scripts
    /// ever call them. To avoid that, <see cref="GlobalsSource.DefaultBindings"/>
    /// is layered underneath: the host's own bindings always win for a given
    /// name, but a name the host hasn't supplied falls back to the prelude's
    /// default implementation.
    /// </summary>
    public ProgramEvaluator(IScriptLexer lexer, IScriptParser parser, ScriptNativeBindings? nativeBindings = null)
      : this(lexer, parser, new FunctionValueFactory(MergeWithGlobalDefaults(nativeBindings)))
    {
    }

    private static ScriptNativeBindings MergeWithGlobalDefaults(ScriptNativeBindings? hostBindings)
    {
      var merged = new ScriptNativeBindings(GlobalsSource.DefaultBindings);

      if (hostBindings != null)
      {
        foreach (var binding in hostBindings)
        {
          merged[binding.Key] = binding.Value;
        }
      }

      return merged;
    }

    /// <summary>
    /// Creates an evaluator with an explicit <see cref="IFunctionValueFactory"/>,
    /// e.g. for testing or to supply a host-specific binding strategy.
    /// </summary>
    public ProgramEvaluator(IScriptLexer lexer, IScriptParser parser, IFunctionValueFactory functionValueFactory)
      : this(lexer, parser, functionValueFactory, new EvaluationComponentFactory())
    {
    }

    /// <summary>
    /// Creates an evaluator with explicit <see cref="IFunctionValueFactory"/> and
    /// <see cref="IEvaluationComponentFactory"/> implementations. Internal — the
    /// component factory deals in the internal collaborator interfaces — but
    /// reachable from tests via <c>InternalsVisibleTo</c>.
    /// </summary>
    internal ProgramEvaluator(IScriptLexer lexer, IScriptParser parser, IFunctionValueFactory functionValueFactory, IEvaluationComponentFactory componentFactory)
    {
      _statements = componentFactory.CreateStatementExecutor(this, functionValueFactory);
      _expressions = componentFactory.CreateExpressionEvaluator(this, functionValueFactory);
      _calls = componentFactory.CreateCallInvoker(this);
      _lexer = lexer;
      _parser = parser;
    }

    public void Evaluate(ModuleGraph graph)
    {
      var globals = new ScriptEnvironment();

      // Seed the reserved "globals.alk" prelude first: ordinary top-level
      // declarations executed into the root environment, so its bindings are
      // true globals — visible to (and shadowable by) the entry module without
      // any "import" or per-script "native function" re-declaration. Compiled
      // once and cached, since its source never changes across evaluations.
      _globalsProgram ??= _parser.ParseTokens(_lexer.Tokenize(GlobalsSource.Text));

      foreach (var declaration in _globalsProgram.Declarations)
      {
        _statements.Execute(declaration, globals);

        if (_signal != null)
        {
          break;
        }
      }

      _signal = null;

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
