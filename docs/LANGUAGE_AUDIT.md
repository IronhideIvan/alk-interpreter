Let me do a thorough audit — checking what's actually wired end-to-end versus what's only partially present:

Here is a structured gap analysis across four categories — things fully implemented, things parsed but silently ignored, missing operators, and missing statements/expressions/features.

---

## Parsed but silently ignored at runtime

These are the most dangerous gaps because they look correct syntactically but produce wrong behaviour:

| Feature | Status |
|---|---|
| **Class field declarations** (`public int count = 0;`) | Parsed into `FieldDecl` AST nodes, but `CallInvoker.Construct` never walks `Members` to initialise them — the field simply doesn't exist on the instance until someone assigns it |
| **Access modifiers** (`private`, `protected`) | Stored in AST but never enforced; any code can read or write any member |
| **Generics** (`class List<T>`, `function<T> T first(...)`) | Parsed and stored in `TypeParameters`; completely erased at runtime — they act as unenforced documentation |
| **Nullable types** (`string? name`) | The `?` suffix is parsed; no null-check is performed at runtime |

---

## Missing operators

| Operator | Example | Notes |
|---|---|---|
| **Compound assignment** | `i += 1`, `x *= 2`, `s -= 3` | The most impactful gap — every loop currently writes `i = i + 1` |
| **Ternary** | `x > 0 ? x : -x` | `Question` token exists for nullable; ternary is entirely absent |
| **Null coalescing** | `name ?? "default"` | Common defensive pattern |
| **Null-conditional access** | `obj?.method()` | Safe navigation on potentially-null references |
| **Bitwise** | `flags & 0xFF`, `a \| b`, `x ^ mask`, `~n`, `n << 2`, `n >> 1` | No bitwise tokens at all |
| **Compound bitwise assignment** | `flags &= mask`, `flags \|= bit` | Follows from the above |

---

## Missing statements

| Feature | Notes |
|---|---|
| **`do...while`** | The only loop form not present |
| **`switch` / `match`** | Pattern-based branching; currently requires chained `if`/`else if` |
| **`foreach` / `for...of`** | Iterating a collection currently requires a manual index counter |

---

## Missing expressions

| Feature | Example | Notes |
|---|---|---|
| **Lambda / anonymous function** | `var double = (x) => x * 2;` | No arrow-function syntax; all callables must be named declarations |
| **String interpolation** | `` `Hello ${name}` `` or `$"Hello {name}"` | Currently requires explicit `+` concatenation |

---

## Missing class / OOP features

| Feature | Notes |
|---|---|
| **Static members** | No `static` keyword; all state is per-instance |
| **Properties** (getter/setter) | No `get`/`set` syntax; fields are exposed directly |
| **Constructor overloading** | Only one constructor per class |
| **Constructor chaining** | `this(args)` to delegate to another constructor |

---

## Missing module features

| Feature | Notes |
|---|---|
| **Default exports** | `export default class Foo` / `import Foo from "mod"` |
| **Re-exports** | `export { Foo } from "./foo"` |

---

## Summary by priority

**Highest impact on day-to-day scripting:**
1. Compound assignment operators — affects almost every loop and accumulator
2. `FieldDecl` initialisation — declared fields silently don't exist
3. Ternary operator — single-expression conditional
4. `foreach`/`for...of` — iterating arrays without manual indexing

**Medium impact:**
5. Lambda expressions — enabling callbacks, higher-order functions, event handlers
6. `do...while` and `switch`
7. Null coalescing `??` and null-conditional `?.`
8. Access modifier enforcement

**Lower priority / advanced:**
10. Static members, properties
11. Bitwise operators
12. String interpolation
13. Generics enforcement (currently type-erased)