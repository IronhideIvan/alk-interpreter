# ALKScript.Interpreter.Runtime

The primary host-facing entry point for running ALKScript programs. Acts as an orchestrator: multiple scripts — and multiple concurrent evaluation instances of the same script — can be started at any time, including between game ticks. All active evaluations share one scheduler, so a single `Pump()` advances every script that has work ready.

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

## Caching compiled scripts

`LoadFromSource` and `LoadFromFile` compile a program into a `ModuleGraph` without starting execution. Pass the graph to `RunFromGraph` each time you want to run it — the lexing and parsing step is skipped on every subsequent run:

```csharp
var runtime = new ProgramRuntime();

// Compile once, at startup or asset-load time.
ModuleGraph hitEffect = runtime.LoadFromFile("effects/hit.alk");

// Later — run a fresh instance each time it's needed,
// with no recompilation cost.
runtime.RunFromGraph(hitEffect);
runtime.RunFromGraph(hitEffect); // second concurrent instance
```

Each call to `RunFromGraph` starts an independent evaluation with its own state — they do not share any runtime data.

## Multiple concurrent scripts

Each call to `RunFromSource` or `RunFromFile` starts a new, independent evaluation. All evaluations share the same scheduler, so one `Pump()` call per tick is enough to advance all of them:

```csharp
var runtime = new ProgramRuntime();

var ui   = runtime.RunFromFile("ui.alk");
var game = runtime.RunFromFile("game.alk");

while (!ui.IsCompleted || !game.IsCompleted)
{
    // Advances every active evaluation in one pass.
    runtime.Pump();

    // New scripts can be kicked off at any point between pumps.
    if (someCondition)
    {
        runtime.RunFromFile("cutscene.alk");
    }
}
```

## Injecting native bindings

Core modules can declare `native function` and `native` method stubs that the host must implement. Register the implementations on `NativeBindings` and `NativeMethodBindings` before running any scripts that use them.

### Native functions

A `native function` is a free-standing function declared in ALKScript and backed by the host:

```alkscript
// In a core module loaded by the host:
native function void log(Object message);
native function Number random();
```

```csharp
var runtime = new ProgramRuntime();

runtime.NativeBindings["log"]    = args => { Console.WriteLine(args[0]); return NullValue.Instance; };
runtime.NativeBindings["random"] = args => new NumberValue(Random.Shared.NextDouble());

runtime.RunUntilComplete(runtime.RunFromFile("game.alk"));
```

Bindings can be added or replaced at any time; the tables are read at `RunFromSource`/`RunFromFile` call time.

### Native methods

A `native` method is declared inside an ALKScript class and keyed by class name and member name:

```alkscript
// In a core module:
native class Sprite {
    native function void moveTo(Number x, Number y);
    native function void destroy();
}
```

```csharp
runtime.NativeMethodBindings["Sprite", "moveTo"]  = (receiver, args) => { /* move the sprite */ return NullValue.Instance; };
runtime.NativeMethodBindings["Sprite", "destroy"] = (receiver, args) => { /* destroy it      */ return NullValue.Instance; };
```

The `receiver` argument is the script-side object the method was called on. Different classes can declare a method with the same name — the two-part key keeps them distinct.

## Advanced usage

For tests or embeddings that need a custom loader, scheduler, or evaluator factory, use the overloaded constructor:

```csharp
var runtime = new ProgramRuntime(loader, scheduler, loop, factory);
```

`scheduler` and `loop` should typically be the same `ScriptScheduler` instance, since the loop must drain the continuations that the scheduler enqueues.
