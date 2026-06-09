using System.Runtime.CompilerServices;

// The Evaluator project is the only assembly that implements IScriptScheduler
// and wraps ScriptEvaluation. Granting it internal visibility lets
// ScriptScheduler.RunUntilComplete access ScriptEvaluation.Task without
// exposing Task on the public surface of ScriptEvaluation.
[assembly: InternalsVisibleTo("ALKScript.Interpreter.Evaluator")]
