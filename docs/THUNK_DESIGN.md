# Async/Await Redesign: `thunk<T>`-typed natives, drop `async`

## Context

ALKScript's current async model (`docs/ASYNC_AWAIT_DESIGN.md`, `docs/LANGUAGE_SPEC.md` §8) has
script-defined `async function int fetchData()` declare an *unwrapped* return type (`int`),
with the `async` modifier causing the runtime to eagerly run the body and wrap the result in a
`TaskValue`. Separately, `async native` *free functions* are lazy/deferred (`PendingOperationValue`,
not started until `await` or end-of-script `Discard`).

Discussion concluded:

- ALKScript scripts are single-threaded and **cannot create a deferred operation themselves** —
  the only source of one is a `native` declaration (host-bound). Script functions can only
  *forward*/*hold*/*await* such values they received from a native call.
- Given that, `async` carries **no real meaning** for script-defined functions/methods/lambdas:
  any function can use `await` on a deferred-operation expression internally and just return a
  plain `T` — there's no need for a separate "this function is async" declaration or a "must
  contain await" check.
- The wrapper type should become a real, **explicit, writable return type**, only meaningful for
  `native` declarations and for script functions that forward such a value, and `await` becomes
  the single, universally-valid operator for unwrapping it to `T`.
- **Naming**: rename this concept from `Task`/`Task<T>` to **`thunk`/`thunk<T>`** (lowercase,
  matching the existing naming convention for keyword-like types such as `lambda<...>`,
  `int`, `string`) — better reflects "a not-yet-run deferred computation" than the C#/JS-derived
  "Task". The script-visible type name becomes `thunk`/`thunk<T>`. The underlying C# runtime
  type/strings are renamed to match: `TaskValue` → `ThunkValue` (C# class names stay PascalCase
  per .NET convention, as `ALKScriptTokenType.Async` already did for the lowercase `async`
  keyword), and `TypeName => "Task"` → `TypeName => "thunk"` on both `ThunkValue` and
  `PendingOperationValue`.
- **`thunk` is a reserved keyword** — added to the lexer's keyword table (own token type, like
  `lambda`/`int`/`string`), not just a recognized identifier-based type name. This avoids any
  collision with a user-defined `class thunk`/identifier named `thunk`.
- The `async` keyword is removed entirely from the grammar (per user direction — simpler and more
  honest than keeping it as a no-op).
- End-of-script `Discard`/fire-and-forget remains **native-only** (`PendingOperationValue`,
  unchanged) — script functions never construct deferred values themselves, so there is nothing
  new to discard.

The net effect is a **simplification**: most of the runtime dispatch (`EvalAwait`, `AwaitIfNeeded`,
`EvalWhenAll`, `PendingOperationValue`, `IAsyncOperationBinder`, `Discard`) is **unchanged in
behavior** (only the `TypeName`/class-name strings change) — it already operates on
`TypeName == "Task"` (→ `"thunk"`) regardless of declared types. The work is: (1) add `thunk` as
a reserved type keyword recognized by the type-checker, (2) rename `TaskValue`→`ThunkValue` and
`TypeName` strings, (3) remove `async` from the grammar/AST/runtime dispatch, (4) re-point the
native lazy-vs-eager binding switch from `IsAsync` to "declared return type is
`thunk`/`thunk<T>`", (5) migrate the handful of fixtures and the spec, and (6) simplify the
Phase-2 `map`/`filter` async-callback machinery, which becomes unnecessary under this model.

## Design details

### `thunk`/`thunk<T>` as a reserved, erased type

- `ALKScript.Interpreter.Common/Token/ALKScriptTokenType.cs` — add a `Thunk` token type
  (alongside `Int`, `Long`, `String`, etc. — the primitive-type tokens; C# enum member name is
  PascalCase per convention, same as existing `Async`/`Lambda` token types for lowercase
  keywords).
- `ALKScript.Interpreter.Lexer/ALKScriptLexer.cs` — add `"thunk" -> ALKScriptTokenType.Thunk`
  (lowercase lexeme, matching `lambda`, `int`, `string`, etc.).
- `ALKScript.Interpreter.Parser/ALKScriptParser.cs`'s `ParseType()` — handle the `Thunk` token
  the same way other type-keyword tokens are handled: consume it, set `TypeNode.Name = "thunk"`,
  then (like `lambda<...>`) optionally parse `"<" type ">"` for `thunk<T>`, or zero type
  arguments for bare `thunk`. `TypeNode` already supports arbitrary `Name<TypeArgs...>` — no AST
  changes needed beyond `ParseType` accepting the new token.
- `ALKScript.Interpreter.Evaluator/Support/TypeChecking.cs`'s `MatchesType` — add a case
  alongside `int`/`string`/etc.:
  ```csharp
  case "thunk":
    return value.TypeName == "thunk";
  ```
  Type-erased on `T` (consistent with `Box<T>`/array element types) — `thunk<int>` and
  `thunk<string>` both just check `TypeName == "thunk"`, matching both `ThunkValue` and
  `PendingOperationValue`. No change needed to `EnsureAssignable` or
  `StatementExecutor.ExecuteReturn` beyond this — `return` statements for a `thunk<T>`-declared
  function are checked the same way as any other type, against the actual `thunk`-shaped value
  (never an unwrapped `T`, since scripts can't produce a bare `thunk` from a `T`).
- `MatchesLambdaType`'s existing `TypesEqual` deep-structural comparison already handles
  `lambda<thunk<int>, int>` correctly once a function's `ReturnType` is literally `thunk<int>` —
  no changes needed there.

### Rename `TaskValue` → `ThunkValue`, `TypeName "Task"` → `"thunk"`

- `ALKScript.Interpreter.Common/Evaluation/Values/TaskValue.cs` → rename file/class to
  `ThunkValue`; `TypeName => "thunk"`.
- `ALKScript.Interpreter.Common/Evaluation/Values/PendingOperationValue.cs`: `TypeName =>
  "thunk"` (was `"Task"`).
- Update all doc comments referencing "Task"/"TaskValue" in `PendingOperationValue.cs`,
  `IAsyncOperationBinder.cs`, `PendingOperation.cs`, `ASYNC_AWAIT_DESIGN.md` to "thunk"/
  "ThunkValue" terminology.
- Grep for all `is TaskValue` / `new TaskValue(` / `"Task"` literal-string occurrences across
  `ALKScript.Interpreter.Evaluator/Evaluation/ExpressionEvaluator.cs`,
  `ALKScript.Interpreter.Evaluator/Evaluation/CallInvoker.cs`, and test files
  (`Tests/Tests.ALKScript.Interpreter.Runtime/IntegrationTests.cs`,
  `Tests/Tests.ALKScript.Interpreter.Runtime/EndToEndTests.cs`) and rename consistently.

### Remove `async` from lexer/parser/AST

- `ALKScript.Interpreter.Lexer/ALKScriptLexer.cs` — remove the `"async" -> Async` keyword
  mapping (so `async` becomes an ordinary identifier).
- `ALKScript.Interpreter.Common/Token/ALKScriptTokenType.cs` — remove `Async`.
- `ALKScript.Interpreter.Common/Ast/FunctionDecl.cs`, `MethodDecl.cs`, `LambdaExpr.cs` — remove
  the `IsAsync` property and constructor parameter.
- `ALKScript.Interpreter.Parser/ALKScriptParser.cs`:
  - Remove `asyncKeyword`/`isAsync` parsing in `ParseMethodOrFieldDecl` (~line 410-411, 442-450),
    `ParseFunctionDecl` (~line 532-533, 549-557), and lambda parsing (~line 1593-1596,
    `LambdaExpr` construction at 1598).
  - Remove `CheckFunctionDeclStart`'s `Async` lookahead (~line 521-524).
  - Remove the "must contain at least one `await`" checks (the three `Error(asyncKeyword!, ...)`
    sites above) — `await` no longer requires any declaration-level marker.
  - Remove the `await`-only-in-async-context restriction (~line 1313-1315) and the
    `InAsyncBody`/`EnterFunctionBody`/`BodyHasAwait`/`RecordAwait` machinery in the
    `ParsingContext` nested class (~lines 1690-1730+) — `await` becomes parseable as a normal
    unary-position expression anywhere `ParseUnary` runs. (Check whether `EnterFunctionBody`'s
    call sites are used for anything *other* than async tracking before deleting wholesale —
    if they're purely async-tracking, remove the calls too.)

### Re-point native lazy/eager dispatch

In `ALKScript.Interpreter.Evaluator/Support/FunctionValueFactory.cs`:

- `Create` (line 58): replace `if (declaration.IsAsync)` with
  `if (declaration.ReturnType.Name == "thunk")` — a free `native` function declared
  `thunk`/`thunk<T>`-returning is bound via `CreatePendingOperationFactory` (unchanged,
  `PendingOperationValue`-producing); otherwise via the ordinary `_nativeBindings` eager path
  (unchanged).
- `MethodAsFunctionDecl` (line 145-155): drop the `method.IsAsync` argument (now that
  `FunctionDecl`'s `IsAsync` is removed). Native *methods* are unaffected either way (per the
  existing doc comment, `native async` methods were already left on the eager
  `_nativeMethodBindings` path — that doesn't change; the host implementation still decides
  whether to return a `ThunkValue`/`PendingOperationValue`, now signalled purely by the declared
  `thunk<T>` return type rather than `async`).

### `CallInvoker` simplification

In `ALKScript.Interpreter.Evaluator/Evaluation/CallInvoker.cs`, `InvokeFunction` (~line 168-211):
remove the `if (function.Declaration.IsAsync) { return Task.FromResult<ALKScriptValue>(new
TaskValue(bodyTask)); }` branch — every function now just runs `RunBody` and returns its task
directly (the current "non-async" path becomes the only path). `ThunkValue` is no longer
constructed for script functions at all (it remains used for natives that resolve via
`NativeAsyncFunctionValue`/eager `Task<ALKScriptValue>`, e.g. `EndToEndTests`'s
`HttpClient.get` binding — unchanged, just renamed).

`ExpressionEvaluator.cs:1152` (`isAsync: lambda.IsAsync` when building a `FunctionDecl` from a
`LambdaExpr`) is removed along with the `IsAsync` field.

### `EvalAwait`/`AwaitIfNeeded`/`EvalWhenAll`/`PendingOperationValue`/`Discard`

**No behavioral changes** beyond the `ThunkValue`/`"thunk"` rename. These already dispatch on
concrete `ThunkValue`/`PendingOperationValue` types and `TypeName == "thunk"`, independent of how
the value was declared. `await` remains valid on any expression evaluating to a `thunk`-shaped
value, in any function (top-level included).

### `array.map`/`array.filter` simplification

The Phase-2 additions for "callback may itself be async" (`AwaitIfNeeded` applied to map/filter
results, `NativeAsyncFunctionValue`/`NativeAsyncFunctionImplementation`) become unnecessary:
under the new model, a callback that needs to `await` a native `thunk<T>` internally is just a
plain `lambda<R, T>` that awaits and returns `R` directly — no special-casing needed in `map`/
`filter`. In `ExpressionEvaluator.cs`'s `GetArrayMember` (`map`/`filter` cases, ~lines
1290-1330): remove the `AwaitIfNeeded` calls on callback results. Check whether
`NativeAsyncFunctionValue`/`NativeAsyncFunctionImplementation` (added in Phase 2, under
`ALKScript.Interpreter.Common/Evaluation/Values/`) are used anywhere else; if not, remove them
and the corresponding `MatchesLambdaType`/`Invoke` cases (`CallInvoker.cs:155-156`,
`TypeChecking.cs:159-161`). Keep `AwaitIfNeeded` itself only if `EvalAwait`'s default case still
needs it (likely yes, for `await` on a non-array `thunk` expression) — otherwise inline it back
into `EvalAwait`.

### Fixture migration (only ~6 declarations + 2 lambda signatures)

- `Tests/.../AsyncFetcher/network.alk:6`: `public native async function string get(string url);`
  → `public native function thunk<string> get(string url);`
- `Tests/.../ItemProcessor/io.alk:13`: `export native async function string process(string name);`
  → `export native function thunk<string> process(string name);`
- `Tests/.../PumpOrdering/sensor.alk:3`: `public native async function int read();`
  → `public native function thunk<int> read();`
- `Tests/.../LambdaShowcase/console.alk:2`: `export native async function int delayValue(int value);`
  → `export native function thunk<int> delayValue(int value);`
- `Tests/.../LambdaShowcase/main.alk:51-56` (`doubleAsync`): rewrite as a plain
  `lambda<int, int>` that `await`s `delayValue` internally and returns `int` directly — no
  `await` needed at the call site:
  ```
  lambda<int, int> doubleAsync = int (int x) => {
      int value = await delayValue(x);
      return value * 2;
  };
  int doubled = doubleAsync(5);
  ```
- `Tests/.../LambdaShowcase/main.alk:68-72` (`asyncDoubled` map callback): drop `async` from the
  lambda signature (`int (int n) => { int value = await delayValue(n); return value * 2; }`) —
  behavior/output unchanged.
- Update accompanying comments in these files (and `AsyncFetcher/main.alk`, `ItemProcessor/main.alk`)
  that describe the old `async`/`TaskValue`-eager-wrapping behavior, using `thunk` terminology.

### Parser/evaluator unit tests referencing `IsAsync`

Update or remove assertions in `Tests/Tests.ALKScript.Interpreter.Parser/FunctionsAndControlFlowTests.cs`,
`LambdaTests.cs`, and `Tests/Tests.ALKScript.Interpreter.Evaluator/Unit/FunctionValueFactoryTests.cs`
that reference `IsAsync` / parse `async function ...` — replace with `thunk<T>`-typed
declarations where the test's intent was "this is an async-style function," or remove if the
test was specifically about `async` parsing/validation (e.g. the "must contain await" error
tests). Also add/extend lexer-level tests for the new `thunk` keyword token.

### Spec update

`docs/LANGUAGE_SPEC.md`:
- §1.2 keyword list: remove `async`, add `thunk`.
- §8 "Async / Await": rewrite to describe the new model — `thunk`/`thunk<T>` as a reserved type
  (only producible by `native` declarations), `await` as a universally-valid unwrapping
  operator, `await [a, b]` (`whenAll`) unchanged, end-of-script fire-and-forget for un-awaited
  native `thunk`s unchanged.
- §2.4 (`lambda<...>`): note that `lambda<thunk<int>, int>` is how a function-typed slot holding
  a `thunk`-forwarding callable would be expressed, vs. `lambda<int, int>` for a callable that
  awaits internally and returns `int`.
- §2.2 array `map`/`filter` rows: drop the "the callback may itself be async" wording (now just
  ordinary `lambda<R, T>`/`lambda<bool, T>`, which may use `await` internally like any function).
- `docs/ASYNC_AWAIT_DESIGN.md`: add a note/addendum recording this revision to the original
  decisions (script-function eager `TaskValue`-wrapping is removed; `async` keyword removed;
  `thunk<T>` is now an explicit reserved type, native-only as a value source; `TaskValue`/
  `"Task"` renamed to `ThunkValue`/`"thunk"`).

## Verification

1. `dotnet build` — confirm no leftover references to `IsAsync`/`Async` token type/`TaskValue`
   compile-error.
2. `dotnet test` — full suite (currently 618 passing) should pass after fixture/test migration.
   Pay particular attention to:
   - `Tests.ALKScript.Interpreter.Parser` (lexer/parser tests for `async`/`await`/new `thunk`
     keyword)
   - `Tests.ALKScript.Interpreter.Evaluator` (`AsyncEvaluationTests`, `ScriptSchedulerTests`,
     `ReplayLogTests`, `FunctionValueFactoryTests`)
   - `Tests.ALKScript.Interpreter.Runtime` end-to-end tests: `AsyncFetcher`, `ItemProcessor`,
     `PumpOrdering`, `LambdaShowcase`, `ArrayMethodsShowcase`
3. Spot-check that `await` on a non-`thunk` value (e.g. `await 5`) now produces a clear
   `RuntimeException`/type error (previously may have been a silent no-op via `AwaitIfNeeded`'s
   default case) — confirm this is the desired tightened behavior, or document if intentionally
   left lenient.