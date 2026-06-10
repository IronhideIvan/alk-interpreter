Gap analysis of ALKScript: what's still missing or incomplete, end-to-end (parser → AST → evaluator).

---

## Implemented (for reference)

Everything below was previously listed as missing and is now fully implemented and tested:

- Compound assignment (`+=`, `-=`, `*=`, `/=`, `&=`, `|=`, `^=`)
- Ternary (`?:`), null coalescing (`??`), null-conditional access (`?.`)
- Bitwise operators (`&`, `|`, `^`, `~`, `<<`, `>>`)
- `do...while`, `switch`, `foreach`
- String interpolation (`` `Hello ${name}` ``)
- `try`/`catch`/`finally`
- `FieldDecl` initialization on `new`
- Access modifier enforcement (`private`, `protected`)
- `sealed` classes, `interface`, `enum`, `module` imports
- `is`/`as` type testing/casting, plus C-style numeric casts (`(int)`, `(long)`, `(float)`)
- Nullable type enforcement (declarations, assignments, fields, parameters, return values)
- Generics enforcement for class/interface type parameters (`new Box<int>(...)`)
- Namespace imports (`import * as Foo from "./foo"`)
- Re-exports (`export { Foo, Bar as Baz } from "./foo"`)
- Static fields and methods (`static`, accessed as `ClassName.member`; classes themselves cannot be `static`)
- Lambdas (`lambda<ReturnType, ParamTypes...>` type, `ReturnType (params) => { ... }` expressions, including `async` lambdas and `this`/`base` capture)
