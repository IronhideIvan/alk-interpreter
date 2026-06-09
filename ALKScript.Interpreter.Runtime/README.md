# ALKScript.Interpreter.Runtime

The primary host-facing entry point for running ALKScript programs. Wraps the full pipeline — lexing, parsing, module loading, and evaluation — behind a single class.

## Basic usage

```csharp
var runtime = new ProgramRuntime();

// Run a script from a source file
var eval = runtime.RunFromFile("game.alk");

// Option 1 — blocking (tools, CLIs, tests)
runtime.RunUntilComplete(eval);

// Option 2 — game loop (call once per tick)
while (!eval.IsCompleted)
{
    runtime.Pump();
}
```

`RunFromSource` works the same way but accepts raw ALKScript source text instead of a file path. Note that relative-path imports are not supported in source mode; use `RunFromFile` for programs that span multiple modules.

## Advanced usage

For tests or embeddings that need custom loaders, evaluators, or schedulers, use the overloaded constructor:

```csharp
var runtime = new ProgramRuntime(loader, evaluator, loop);
```
