using System;
using ALKScript.Interpreter.Common.Evaluation.Values;

namespace ALKScript.Interpreter.Evaluator
{
  /// <summary>
  /// The reserved "globals.alk" prelude — ordinary ALKScript source whose
  /// top-level declarations are executed into the root environment before the
  /// entry module's own declarations run, giving every script a set of "true
  /// global" bindings (callable with no <c>import</c> and no per-script
  /// <c>native function</c> declaration).
  ///
  /// Because it's just ALKScript source put through the normal lex -&gt; parse -&gt;
  /// execute pipeline, declarations here behave exactly like top-level
  /// declarations anywhere else: ordinary functions get real declared arity and
  /// interpreted bodies, and <c>native</c> ones are resolved against the host's
  /// <see cref="ScriptNativeBindings"/> the same way a script's own
  /// <c>native function</c> declarations are — no separate binding surface or
  /// variadic special-casing required. A script can still declare a same-named
  /// top-level binding to shadow one of these, exactly like shadowing any other
  /// enclosing-scope binding.
  ///
  /// <c>native</c> declarations fail as soon as they're declared if the host
  /// hasn't registered a matching implementation (see
  /// <see cref="FunctionValueFactory"/>) — and that check would otherwise run
  /// for *every* evaluation, regardless of whether the script ever calls the
  /// global. To keep the prelude from forcing every host to register bindings
  /// it doesn't care about, <see cref="DefaultBindings"/> supplies a sensible
  /// fallback for each <c>native</c> declaration here; <see cref="ProgramEvaluator"/>
  /// layers the host's own <see cref="ScriptNativeBindings"/> on top, so a host
  /// that wants different behavior (or no console output at all) simply
  /// registers its own binding under the same name to override the default.
  /// </summary>
  internal static class GlobalsSource
  {
    public const string Text =
      "native function void print(Object value);\n";

    /// <summary>
    /// Fallback host implementations for this prelude's <c>native</c>
    /// declarations, used for any name the host hasn't supplied its own
    /// binding for. Writes to <see cref="Console.Out"/> via <see cref="Operators.Stringify"/>.
    /// </summary>
    public static ScriptNativeBindings DefaultBindings => new ScriptNativeBindings
    {
      ["print"] = arguments =>
      {
        Console.Out.WriteLine(Operators.Stringify(arguments[0]));
        return NullValue.Instance;
      }
    };
  }
}
