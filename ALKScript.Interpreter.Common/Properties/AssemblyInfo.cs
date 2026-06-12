using System.Runtime.CompilerServices;

// The Evaluator project is the only assembly that implements IScriptScheduler
// and wraps ScriptEvaluation. Granting it internal visibility lets
// ScriptScheduler.RunUntilComplete access ScriptEvaluation.Task without
// exposing Task on the public surface of ScriptEvaluation.
[assembly: InternalsVisibleTo("ALKScript.Interpreter.Evaluator")]

// Phase B structural Capture/Restore (docs/ASYNC_AWAIT_DESIGN.md Addendum 3)
// reads ScriptEnvironment's "own scope" internals from the Serialization
// project, and from tests in both projects.
[assembly: InternalsVisibleTo("ALKScript.Interpreter.Serialization")]
[assembly: InternalsVisibleTo("Tests.ALKScript.Interpreter.Evaluator")]
[assembly: InternalsVisibleTo("Tests.ALKScript.Interpreter.Serialization")]
