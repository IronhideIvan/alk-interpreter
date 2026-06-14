using System.Runtime.CompilerServices;

// The Evaluator project's CursorProgramEvaluator and Phase A/B Capture/Restore
// need internal access to ScriptEnvironment and related runtime state.
[assembly: InternalsVisibleTo("ALKScript.Interpreter.Evaluator")]

// Phase B structural Capture/Restore (docs/ASYNC_AWAIT_DESIGN.md Addendum 3)
// reads ScriptEnvironment's "own scope" internals from the Serialization
// project, and from tests in both projects.
[assembly: InternalsVisibleTo("ALKScript.Interpreter.Serialization")]
[assembly: InternalsVisibleTo("Tests.ALKScript.Interpreter.Evaluator")]
[assembly: InternalsVisibleTo("Tests.ALKScript.Interpreter.Serialization")]
