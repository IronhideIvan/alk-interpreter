# ALKScript Interpreter

-----
Note by the developer (Arthur Lisek-Koper)

This project exists as a personal experiment to use an LLM to define a programming language grammar,
then build a lexer, parser, interpreter, and runtime. I've been wanting to build a little scripting language
for some personal projects I've been working on, but the amount of free time I have build something of that
scope seems to dwindle a little bit every day.. So I decided this was a good time to play around with AI coding
and see what it can do. 

The amount of actual code and text provided directly by the developer (me) is minimal. I did spend a fair amount of time
in design because, at the end of day, this is something that I wanted to provide practical value to me and solve problems
that I wanted solved. 

I don't expect this to be useful to anyone other than me.. But if anyone does use it, I'd be happy to hear about it :) for
the sake of my own curiosity :).

If you have any questions/comments, you can reach out to me at alkfreelance@outlook.com

That's all. 99% of everything else in this project is LLM generated. 
-----

ALKScript is a small, embeddable scripting language designed to be hosted
inside a larger application (games, tools, simulations, etc.). This
repository contains the full implementation — lexer, parser, evaluator, and
a host-facing runtime — along with a sample application showing how to embed
it.

## Why ALKScript?

ALKScript is built around two ideas that make it a good fit for embedding in
an interactive application (especially games):

- **Cooperative async execution.** Scripts can `await` long-running,
  host-defined operations (`native async` functions) without blocking the
  host. A single `Pump()` call advances every running script by one step,
  which maps naturally onto a game loop or tick-based simulation.
- **Structural state serialization.** A suspended script's entire execution
  state (call stack, locals, pending awaits) can be captured to bytes and
  later restored — enabling save/load of in-progress scripts, not just the
  data they operate on.

The language itself is a small, statically-typed, C-like language with
classes, interfaces, generics, lambdas, modules (`import`/`export`), and the
usual control-flow and operator set. See
[docs/LANGUAGE_SPEC.md](docs/LANGUAGE_SPEC.md) for the full language
reference and [docs/ASYNC_AWAIT_DESIGN.md](docs/ASYNC_AWAIT_DESIGN.md) for
the design behind the async/await and scheduling model.

## Project layout

| Project | Purpose |
| --- | --- |
| `ALKScript.Interpreter.Lexer` | Tokenizes ALKScript source text. |
| `ALKScript.Interpreter.Parser` | Builds an AST and module graph from tokens. |
| `ALKScript.Interpreter.Common` | Shared AST, token, evaluation-value, and module types used across the pipeline. |
| `ALKScript.Interpreter.Evaluator` | Tree-walking, cursor-based evaluator that executes the AST and supports suspension/resumption. |
| `ALKScript.Interpreter.Runtime` | The primary host-facing API (`ProgramRuntime`) — ties the pipeline together and manages native bindings, scheduling, and concurrent script execution. |
| `ALKScript.Interpreter.Serialization` | Captures and restores a suspended evaluator's state for save/load support. |
| `ALKScript.Interpreter.Console` | Minimal console entry point used during development. |
| `ALK.Interpreter.Sample` | A small terminal-based sample game demonstrating how to embed the runtime, expose native bindings, and persist save state. |
| `Tests/*` | Unit and integration tests for each project above. |

## Getting started

### Prerequisites

- [.NET SDK](https://dotnet.microsoft.com/) (see the `*.csproj` files for the
  target framework).

### Build and test

```sh
dotnet build ALKScript.Interpreter.sln
dotnet test ALKScript.Interpreter.sln
```

### Run the sample

```sh
dotnet run --project ALK.Interpreter.Sample
```

This launches a small terminal roguelike whose enemy AI and interactable
objects are written in ALKScript (see `ALK.Interpreter.Sample/Assets`). It
demonstrates loading scripts, ticking them once per turn, and saving/loading
mid-execution state.

## Embedding the interpreter in your own application

The typical entry point is `ProgramRuntime` from
`ALKScript.Interpreter.Runtime`:

```csharp
using ALKScript.Interpreter.Runtime;

var runtime = new ProgramRuntime();

// Run a script from a source file (or RunFromSource for raw text)
var run = runtime.RunFromFile("game.alk");

// Blocking — useful for tools, CLIs, or tests
runtime.RunUntilComplete(run);

// Or drive it from a game loop, one step per tick
while (!run.IsCompleted)
{
    runtime.Pump();
}
```

To compile a script once and run it multiple times (e.g. shared AI
behaviors), use `LoadFromFile`/`LoadFromSource` to produce a `ModuleGraph`,
then call `RunFromGraph` for each instance.

### Native bindings

Scripts declare `native function`/`native` methods that the host must
implement, registered via `runtime.NativeBindings` and
`runtime.NativeMethodBindings`:

```csharp
runtime.NativeBindings["log"] = args =>
{
    Console.WriteLine(((StringValue)args[0]).Value);
    return NullValue.Instance;
};
```

### Async operations and save/load

Programs that `await` host-defined `native async` operations need an
`IAsyncOperationBinder` (set via `runtime.OperationBinder`) to start, poll,
and resolve those operations. A suspended evaluator's state can be captured
and restored using `CursorStructuralStateSerializer` from
`ALKScript.Interpreter.Serialization`, enabling persistent save/load of
in-progress scripts.

For a complete, working example of all of the above — native bindings, core
modules, per-tick execution, and save/load — see
[ALK.Interpreter.Sample/ScriptHost.cs](ALK.Interpreter.Sample/ScriptHost.cs).

For a deeper dive into the runtime API itself, see
[ALKScript.Interpreter.Runtime/README.md](ALKScript.Interpreter.Runtime/README.md).
