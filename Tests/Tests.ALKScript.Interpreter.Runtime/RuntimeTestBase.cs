using System.Collections.Generic;
using ALKScript.Interpreter.Common;
using ALKScript.Interpreter.Common.Evaluation.Scheduling;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Evaluator;
using ALKScript.Interpreter.Evaluator.Scheduling;
using ALKScript.Interpreter.Lexer;
using ALKScript.Interpreter.Parser;
using ALKScript.Interpreter.Parser.Modules;
using ALKScript.Interpreter.Runtime;
using Tests.ALKScript.Interpreter.Runtime.Support;

namespace Tests.ALKScript.Interpreter.Runtime;

/// <summary>
/// Shared helper for running ALKScript source through <see cref="ProgramRuntime"/>
/// and observing the values it produces.
///
/// Tests observe results by declaring a host-bound <c>record</c> native function
/// (see <see cref="RecordDeclaration"/>) and calling it with the value(s) under
/// test; <see cref="RunFromSource"/> and <see cref="RunFromFile"/> return every
/// value passed to it, in call order.
/// </summary>
public abstract class RuntimeTestBase
{
  protected const string RecordDeclaration = "native function void record(Object value);\n";

  protected static IReadOnlyList<ALKScriptValue> RunFromSource(
    string source,
    ScriptNativeBindings? extraBindings = null,
    IReadOnlyDictionary<string, string>? coreModules = null,
    IGlobalPreludeProvider? preludes = null)
  {
    var runtime = CreateRuntime(
      files: new Dictionary<string, string>(),
      coreModules: coreModules,
      preludes: preludes,
      extraBindings: extraBindings,
      out var recorded);

    runtime.RunUntilComplete(runtime.RunFromSource(source));
    return recorded;
  }

  protected static IReadOnlyList<ALKScriptValue> RunFromFile(
    string entryFilePath,
    IReadOnlyDictionary<string, string> files,
    ScriptNativeBindings? extraBindings = null,
    IReadOnlyDictionary<string, string>? coreModules = null,
    IGlobalPreludeProvider? preludes = null)
  {
    var runtime = CreateRuntime(
      files: files,
      coreModules: coreModules,
      preludes: preludes,
      extraBindings: extraBindings,
      out var recorded);

    runtime.RunUntilComplete(runtime.RunFromFile(entryFilePath));
    return recorded;
  }

  /// <summary>
  /// Creates a runtime whose evaluation is not yet driven, so the caller can
  /// inspect <see cref="ScriptEvaluation.IsCompleted"/> before and after
  /// calling <see cref="IScriptLoop.RunUntilComplete"/>.
  /// </summary>
  protected static ProgramRuntime CreateRuntimeForEvaluation(
    IReadOnlyDictionary<string, string>? files = null,
    IReadOnlyDictionary<string, string>? coreModules = null,
    IGlobalPreludeProvider? preludes = null,
    ScriptNativeBindings? extraBindings = null)
  {
    return CreateRuntime(
      files: files ?? new Dictionary<string, string>(),
      coreModules: coreModules,
      preludes: preludes,
      extraBindings: extraBindings,
      out _);
  }

  private static ProgramRuntime CreateRuntime(
    IReadOnlyDictionary<string, string> files,
    IReadOnlyDictionary<string, string>? coreModules,
    IGlobalPreludeProvider? preludes,
    ScriptNativeBindings? extraBindings,
    out List<ALKScriptValue> recorded)
  {
    var capturedRecorded = new List<ALKScriptValue>();
    recorded = capturedRecorded;

    var scheduler = new ScriptScheduler();

    var loader = new ProgramLoader(
      new ALKScriptLexer(),
      new ALKScriptParser(),
      new FakeModuleFileReader(files),
      new FakeCoreModuleProvider(coreModules ?? new Dictionary<string, string>()),
      preludes);

    var runtime = new ProgramRuntime(loader, scheduler, scheduler, new EvaluatorFactory());

    runtime.NativeBindings["record"] = args => { capturedRecorded.Add(args[0]); return NullValue.Instance; };

    if (extraBindings != null)
    {
      foreach (var binding in extraBindings)
      {
        runtime.NativeBindings[binding.Key] = binding.Value;
      }
    }

    return runtime;
  }
}
