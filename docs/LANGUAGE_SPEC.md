# ALKScript Language Specification

This document describes the ALKScript language as implemented by the lexer,
parser, and evaluator in this repository. It is a description of the
*current* implementation, not an aspirational design document — every
construct described here is exercised by the test suite (see
`Tests/Tests.ALKScript.Interpreter.Runtime/ALKScripts/*` for runnable
examples).

---

## 1. Lexical Structure

### 1.1 Comments

```
// line comment, runs to end of line
/* block comment, may span
   multiple lines (not nestable) */
```

### 1.2 Identifiers and Keywords

An identifier starts with a letter (`a`-`z`, `A`-`Z`) or underscore (`_`),
followed by any number of letters, digits, or underscores.

The following words are reserved keywords and cannot be used as identifiers:

```
if else while for foreach in do break continue
switch case default
function native return var const true false null
await thunk
try catch finally throw
int long float string bool void
class new this base extends
public protected private virtual abstract override sealed
interface implements enum static readonly lambda
is
import export from as
```

### 1.3 Literals

| Kind | Examples | Notes |
| --- | --- | --- |
| Integer | `0`, `42`, `123` | Lexed as a single `Number` token; parsed as `int` if it fits in a 32-bit signed integer, otherwise `long`. |
| Long | `42L`, `42l` | A trailing `L`/`l` forces a `long` literal regardless of magnitude. |
| Float | `3.14`, `0.5` | Any numeric literal containing a `.` is a `float` (double-precision). There is no `F`/`f` suffix. |
| Boolean | `true`, `false` | |
| Null | `null` | Only assignable/comparable where the static type is nullable (`T?`). |
| String | `"hello"`, `"line\nbreak"` | Double-quoted. Supports escapes `\n \t \r \" \\ \0`; any other `\x` sequence yields the literal character `x`. Strings may span multiple physical lines. |
| Interpolated string | `` `Hello ${name}!` `` | Backtick-delimited. `${...}` embeds an arbitrary expression. Supports escapes `` \n \t \r \` \$ \\ \0 ``. May contain multiple `${...}` interpolations and may span multiple lines. |

There is **no dedicated negative-literal token**: `-7` is parsed as the unary
`-` operator applied to the literal `7` (an `int`).

### 1.4 Operators and Punctuation

```
+  -  *  /  %
=  +=  -=  *=  /=  %=  &=  |=  ^=  <<=  >>=
==  !=  <  <=  >  >=
&&  ||  !
&  |  ^  ~  <<  >>
++  --
?  ??  ?.  ?:
=>
is  as
( )  { }  [ ]
,  ;  :  .
```

---

## 2. Types

### 2.1 Primitive Types

| Type | Description |
| --- | --- |
| `int` | 32-bit signed integer. |
| `long` | 64-bit signed integer. `int` and `long` are both backed by the same runtime integer value, so casts between them are no-ops. |
| `float` | Double-precision floating-point number. |
| `string` | UTF-16 text. |
| `bool` | `true` / `false`. |
| `void` | Only valid as a function/method/lambda return type, meaning "no value". |

`s[i]` indexes a string, yielding the single-character `string` at UTF-16
code-unit position `i` (there is no separate `char` type). As with arrays,
`i` must be an `int` and a runtime error is raised if it is out of bounds.
Since strings are immutable, `s[i]` cannot appear as an assignment target
(`s[i] = ...`, `s[i]++`, etc. are all runtime errors).

`string` values also expose a small set of built-in members, accessed like
`s.length`, `s.toUpper()`, etc. All of these return new values; strings are
immutable and none of these mutate the receiver.

| Member | Signature | Behavior |
| --- | --- | --- |
| `length` | property, `int` | Number of UTF-16 code units. |
| `toUpper` | `() -> string` | Uppercased copy (invariant culture). |
| `toLower` | `() -> string` | Lowercased copy (invariant culture). |
| `trim` | `() -> string` | Removes leading/trailing whitespace. |
| `substring` | `(int start, int count) -> string` | Returns the `count`-character substring starting at `start`. Runtime error if the range is out of bounds. |
| `indexOf` | `(string) -> int` | Index of the first occurrence (ordinal comparison), or `-1` if not found. |
| `contains` | `(string) -> bool` | Whether the substring occurs (ordinal comparison). |
| `startsWith` | `(string) -> bool` | Ordinal comparison. |
| `endsWith` | `(string) -> bool` | Ordinal comparison. |
| `split` | `(string separator) -> string[]` | Splits on every occurrence of `separator` (no separator-collapsing). |
| `replace` | `(string old, string new) -> string` | Replaces all occurrences of `old` with `new`. |

### 2.2 Arrays

`T[]` is an array of `T`. Array literals are written `[expr, expr, ...]`.
Arrays are accessed and assigned with `arr[index]`. Multi-dimensional arrays
are written `T[][]`, etc. (each `[]` increments the type's array rank).

Arrays expose a small set of built-in members, accessed like
`arr.length`, `arr.push(x)`, etc.:

| Member | Signature | Behavior |
| --- | --- | --- |
| `length` | property, `int` | Number of elements. |
| `push` | `(T) -> int` | Appends an element in place; returns the new length. |
| `pop` | `() -> T` | Removes and returns the last element in place; runtime error on an empty array. |
| `join` | `(T[]) -> T[]` | Returns a new array containing this array's elements followed by `other`'s; does not mutate either operand. |
| `slice` | `(int, int) -> T[]` | Returns a new array of `count` elements starting at `start`; does not mutate the receiver. Runtime error if the range is out of bounds. |
| `remove` | `(int) -> T` | Removes and returns the element at `index` in place; runtime error if `index` is out of bounds. |
| `map` | `(lambda<R, T>) -> R[]` | Returns a new array containing the result of calling the given callback with each element in turn, in order; does not mutate the receiver. |
| `filter` | `(lambda<bool, T>) -> T[]` | Returns a new array containing only the elements for which the given callback returns `true`, preserving order; does not mutate the receiver. Runtime error if the callback returns a non-`bool` value. |

`map` and `filter` accept any single-argument callable — a `lambda<...>`
expression, a named function/method reference, etc. — and invoke it once per
element. A callback that needs to unwrap a `thunk`/`thunk<T>` value may use
`await` internally like any other function.

These are not subject to the element-type checks described in §2.5 — element
types are erased once values are inside an `ArrayValue`.

### 2.3 Nullable Types

Appending `?` to any type (`int?`, `string?`, `Box<int>?`, `int[]?`, ...)
makes it nullable, i.e. it may also hold `null`.

- Assigning, passing, or returning `null` for a **non-nullable** type is a
  runtime (`RuntimeException`) error.
- `T?` accepts both `null` and any value that is assignable to `T`.

This is enforced at every assignment, field initialization, parameter
binding, and return point (`TypeChecking.EnsureAssignable`).

### 2.4 The `lambda<...>` Function Type

`lambda<ReturnType, ParamType1, ParamType2, ...>` describes a callable value:
the first type argument is the return type, and the remaining type arguments
(zero or more) are the parameter types in order. For example:

```
lambda<int, int, int> add;       // (int, int) -> int
lambda<void> action;             // () -> void
lambda<void, string> printer;    // (string) -> void
```

A value is assignable to a `lambda<...>` type if it is callable with a
matching arity and (for script-defined functions/lambdas, where parameter and
return types are known) matching parameter and return types.

`thunk`/`thunk<T>` (see §8) can appear as a type argument like any other type:
`lambda<thunk<int>, int>` describes a callable that *forwards* a deferred
operation (returns a `thunk<int>` without awaiting it), whereas
`lambda<int, int>` describes a callable that `await`s internally and returns
the unwrapped `int` directly.

### 2.5 Generic Types

Classes, interfaces, and functions/methods may declare type parameters:

```
class Box<T> { ... }
interface IContainer<T> { ... }
function<T> T identity(T value) { return value; }
```

Note the placement: for functions and methods, `<T>` appears **immediately
after the `function` keyword and before the return type** —
`function<T> ReturnType name(...)`.

A generic class **must** be instantiated with explicit type arguments
(`new Box<int>(5)`); `new Box(5)` with no type arguments is a
`RuntimeException`. Once instantiated, every field, parameter, or return
value typed with a bare type parameter (e.g. `T`) is checked against the
concrete type argument recorded for that instance. Compound uses of a type
parameter (e.g. `T[]`, `Array<T>`) are *not* checked — generics are otherwise
type-erased.

### 2.6 `var` and Type Inference

`var name = expr;` declares a variable whose static type is inferred from
`expr` and is not checked again afterwards (i.e. `var` itself imposes no
nullability/type constraint — only explicitly-typed declarations do).

### 2.7 `is`, `as`, and Numeric Casts

- `value is Type` — `true` if `value`'s runtime type matches `Type`. Works
  for primitives (`int`, `long`, `float`, `string`, `bool`), array types
  (checks the value is any array), `lambda<...>` types (arity/signature
  check), classes (including any superclass in the chain), interfaces
  (including interfaces implemented by a superclass, and interfaces extended
  by an implemented interface), and enum types. `null is T?` is `true`;
  `null is T` (non-nullable) is `false`.
- `value as Type` — yields `value` if `value is Type`, otherwise `null`. (The
  static type of an `as` expression is therefore effectively `Type?`.)
- `(int)expr`, `(long)expr`, `(float)expr` — C-style numeric casts.
  Truncates `float` toward zero when converting to `int`/`long`; widens
  `int`/`long` to `float`. Casting between `int` and `long` is a no-op.

---

## 3. Operator Precedence

From loosest (evaluated last) to tightest (evaluated first). Binary operators
are left-associative unless noted otherwise.

| Level | Operators | Associativity |
| --- | --- | --- |
| 1 (loosest) | `=` `+=` `-=` `*=` `/=` `%=` `&=` `\|=` `^=` `<<=` `>>=` | right |
| 2 | `?:` (ternary) | right |
| 3 | `??` | left |
| 4 | `\|\|` | left |
| 5 | `&&` | left |
| 6 | `\|` | left |
| 7 | `^` | left |
| 8 | `&` | left |
| 9 | `==` `!=` | left |
| 10 | `<` `<=` `>` `>=` | left |
| 11 | `is` `as` | left |
| 12 | `<<` `>>` | left |
| 13 | `+` `-` (binary) | left |
| 14 | `*` `/` `%` | left |
| 15 | unary `!` `-` `~`, prefix `++`/`--`, `await`, `(int)`/`(long)`/`(float)` casts | n/a (prefix) |
| 16 (tightest) | call `f(...)`, member `.`, `?.`, index `[...]`, postfix `++`/`--` | left |

Note that `is`/`as` bind *tighter* than comparison operators but *looser*
than shift operators — e.g. `a < b is int` parses as `a < (b is int)`. Since
`is`/`as` are always followed by a *type* (not an arbitrary expression), the
right-hand side simply consumes a type name (with optional `<...>`, `[]`,
`?`) rather than continuing the precedence chain.

The assignment target of `=`/compound-assignment must be an identifier, a
member access (`a.b`), or an index expression (`a[i]`).

---

## 4. Declarations

A program (and each module) is a sequence of top-level declarations:

```
program        = { importDecl } , { declaration } ;

declaration    = exportDecl
               | reExportDecl
               | classDecl
               | interfaceDecl
               | enumDecl
               | functionDecl
               | variableDecl
               | statement ;
```

All `import` declarations must appear before any other declaration.

### 4.1 Variable Declarations

```
variableDecl = [ "const" ] ( "var" | type ) identifier [ "=" expression ] ";" ;
```

```
var x = 5;
int y = 10;
string? name = null;

const int max = 100;
const var label = "fixed";
```

- A `const` declaration **requires** an initializer; omitting it (`const int x;`)
  is a parse-time error.
- After initialization, the name cannot be the target of `=`, any compound
  assignment (`+=`, `-=`, ...), or `++`/`--` — each is a runtime
  (`RuntimeException`) error. This restriction applies to the *binding*
  itself; it does not make the referenced value immutable, so
  `const int[] items = [1, 2]; items.push(3);` and
  `const var box = new Box(1); box.value = 2;` are both allowed.
- `const` is a property of the local/top-level binding, not of the static
  type — there is no `const`-qualified type syntax (e.g. no `const int[]`
  meaning "array of const ints").

### 4.2 Function Declarations

```
functionDecl = [ "export" ] [ "native" ]
               "function" [ "<" typeParamList ">" ] type identifier
               "(" [ paramList ] ")" ( block | ";" ) ;

paramList    = parameter , { "," , parameter } ;
parameter    = type identifier ;
```

```
function int add(int a, int b) {
    return a + b;
}

function<T> T identity(T value) {
    return value;
}

native function void log(string message);   // body-less; host-implemented
```

- The declared return type is the function's *actual* return type — see
  §8 for how `thunk`/`thunk<T>` and `await` work.
- A `native` function has no body (terminated by `;`) and is implemented by
  the embedding host.
- `await` is a universally-valid operator and may appear in the body of any
  function, method, lambda, or at the top level — there is no declaration-level
  marker required to use it.

### 4.3 Top-Level `await`

The entry module's top-level statements may use `await` directly (mirroring
top-level `await` in other async-capable languages).

---

## 5. Statements

```
statement = exprStmt
          | block
          | ifStmt
          | whileStmt
          | doWhileStmt
          | forStmt
          | foreachStmt
          | switchStmt
          | returnStmt
          | breakStmt
          | continueStmt
          | throwStmt
          | tryStmt
          | variableDecl ;

exprStmt   = expression ";" ;
block      = "{" { declaration } "}" ;

ifStmt     = "if" "(" expression ")" statement [ "else" statement ] ;
whileStmt  = "while" "(" expression ")" statement ;
doWhileStmt= "do" statement "while" "(" expression ")" ";" ;

forStmt    = "for" "(" ( variableDecl | exprStmt | ";" )
                       [ expression ] ";"
                       [ expression ] ")" statement ;

foreachStmt = "foreach" "(" "var" identifier "in" expression ")" statement ;

switchStmt = "switch" "(" expression ")" "{"
               { "case" expression ":" { declaration } }
               [ "default" ":" { declaration } ]
             "}" ;

returnStmt   = "return" [ expression ] ";" ;
breakStmt    = "break" ";" ;
continueStmt = "continue" ";" ;
throwStmt    = "throw" expression ";" ;

tryStmt    = "try" block
             { "catch" [ "(" type identifier ")" ] block }
             [ "finally" block ] ;
```

Notes:

- `for` accepts a variable declaration, an expression statement, or nothing
  for its initializer (each followed by the loop's normal `;` separators).
- `foreach` requires `var` — there is no typed-element form
  (`foreach (Type x in ...)` is not supported).
- `switch` cases use ordinary fall-through semantics (no implicit `break`);
  case bodies are parsed as a sequence of declarations/statements, so they
  may themselves contain `var`/local declarations. At most one `default`
  case is allowed, and it may appear anywhere among the cases.
- `try` requires at least one `catch` and/or a `finally`. A `catch` clause
  may omit its `(Type name)` entirely (catching any thrown value without
  binding it), or bind the thrown value to a name with an optional type
  (e.g. `catch (string message)`). `throw` accepts any expression — there is
  no required exception base type (see §10).

---

## 6. Expressions

```
expression = assignment ;

assignment = ( call "." identifier | call "[" expression "]" | identifier )
              ( "=" | "+=" | "-=" | "*=" | "/=" | "%="
              | "&=" | "|=" | "^=" | "<<=" | ">>=" ) assignment
           | ternary ;

ternary    = nullCoalescing [ "?" expression ":" assignment ] ;
nullCoalescing = logicOr { "??" logicOr } ;
logicOr    = logicAnd { "||" logicAnd } ;
logicAnd   = bitwiseOr { "&&" bitwiseOr } ;
bitwiseOr  = bitwiseXor { "|" bitwiseXor } ;
bitwiseXor = bitwiseAnd { "^" bitwiseAnd } ;
bitwiseAnd = equality { "&" equality } ;
equality   = comparison { ( "==" | "!=" ) comparison } ;
comparison = typeTest { ( "<" | "<=" | ">" | ">=" ) typeTest } ;
typeTest   = shift { ( "is" | "as" ) type } ;
shift      = term { ( "<<" | ">>" ) term } ;
term       = factor { ( "+" | "-" ) factor } ;
factor     = unary { ( "*" | "/" | "%" ) unary } ;

unary      = ( "!" | "-" | "~" ) unary
           | ( "++" | "--" ) unary
           | "await" unary
           | "(" ( "int" | "long" | "float" ) ")" unary
           | call ;

call       = primary { "(" [ argList ] ")"
                       | "." identifier
                       | "?." identifier
                       | "[" expression "]"
                       | ( "++" | "--" ) } ;

primary    = "true" | "false" | "null" | NUMBER | STRING
           | interpolatedString
           | "this" | "base"
           | identifier
           | "(" expression ")"
           | arrayLiteral
           | newExpr
           | lambdaExpr ;

arrayLiteral = "[" [ expression { "," expression } ] "]" ;
newExpr      = "new" identifier [ "<" typeArgList ">" ] "(" [ argList ] ")" ;
argList      = expression { "," expression } ;
```

### 6.1 Lambda Expressions

```
lambdaExpr = type "(" [ paramList ] ")" "=>" block ;
```

```
lambda<int, int, int> add = int (int x, int y) => { return x + y; };

lambda<int, int> scale = int (int x) => { return x * factor; };  // captures "factor"

lambda<void, int> action = void (int n) => { log(`item = ${n}`); };

lambda<int, int> doubleValue = int (int x) => {
    int value = await delayValue(x);
    return value * 2;
};
```

- The leading `type` is the lambda's *return type*; the parameter list
  follows in parentheses, then `=>` and a block body.
- A lambda body is a `{ ... }` block (not a single bare expression).
- Lambdas close over (by reference) local variables, parameters, and `this`
  / `base` from the enclosing scope, so mutations to a captured variable are
  visible to the lambda and vice versa.
- `await` may be used inside a lambda body like any function — there is no
  declaration-level marker required (see §8).
- The static type of a lambda expression is `lambda<ReturnType, ParamTypes...>`
  and is checked against the declared type of the variable/field/parameter it
  is assigned to.

### 6.2 Increment/Decrement

`++x` / `--x` (prefix) evaluate to the value *after* the update; `x++` /
`x--` (postfix) evaluate to the value *before* the update. Both are valid on
any assignable target (identifier, member access, index expression).

### 6.3 Null-Conditional Access

`a?.b` evaluates `a`; if it is `null`, the whole expression short-circuits to
`null` without evaluating `b`. Otherwise it behaves like `a.b`.

### 6.4 String Interpolation

`` `text ${expr} more ${expr2}` `` — each `${...}` is evaluated, converted to
its string representation, and substituted into the surrounding literal text.
Interpolated strings may contain any number of `${...}` segments (including
zero, in which case it behaves like a plain string) and may span multiple
lines.

---

## 7. Classes, Interfaces, and Enums

### 7.1 Class Declarations

```
classDecl = [ "export" ] [ "native" ] [ "abstract" | "sealed" ]
            "class" identifier [ "<" typeParamList ">" ]
            [ "extends" identifier [ "<" typeArgList ">" ] ]
            [ "implements" identifier { "," identifier } ]
            "{" { member } "}" ;

member = constructorDecl | fieldDecl | methodDecl ;

constructorDecl = [ accessModifier ] "new" "(" [ paramList ] ")" block ;

fieldDecl = [ accessModifier ] [ "static" ] [ "readonly" ] ( "var" | type ) identifier
            [ "=" expression ] ";" ;

methodDecl = [ accessModifier ] [ "static" ]
             [ "virtual" | "abstract" | "override" ]
             [ "native" ]
             "function" [ "<" typeParamList ">" ] type identifier
             "(" [ paramList ] ")" ( block | ";" ) ;

accessModifier = "public" | "protected" | "private" ;
```

- Members default to `private` if no access modifier is given.
- `abstract` and `sealed` are mutually exclusive on a class.
- `static` cannot be combined with `virtual`, `abstract`, or `override`.
- A member is parsed as a `MethodDecl` if it has an override modifier
  (`virtual`/`abstract`/`override`), is `native`, or starts with
  `function`; otherwise (a bare `[access]? [static]? type name [= init]? ;`)
  it is a `FieldDecl`.
- `abstract` methods have no body (`;` only) and may only appear in
  `abstract` classes.
- A class containing any `native` member must itself be declared `native`.
- A class declared `sealed` cannot be `extends`-ed; doing so is a runtime
  error.
- A field declared `readonly` may only be assigned from within the
  constructor of its declaring class (in addition to its own initializer
  expression, if any). Assigning to a `readonly` field anywhere else —
  including methods of the declaring class, subclasses, or external code —
  is a runtime error. `readonly` cannot be combined with `static`.

```
class Box {
    public string? label = null;
    public int value;

    public new(int value) {
        this.value = value;
    }

    public function int getValue() {
        return this.value;
    }
}
```

### 7.2 Inheritance, `this`, `base`

```
class Animal {
    protected string name;

    public new(string name) {
        this.name = name;
    }

    public virtual function string speak() {
        return this.name + " makes a sound";
    }
}

class Dog extends Animal {
    public new(string name) {
        base(name);          // calls the superclass constructor
    }

    public override function string speak() {
        return this.name + " says Woof";
    }
}
```

- `base(...)` (as a call) invokes the superclass constructor, and must be the
  first statement of a derived class's constructor if used.
- `base.method(...)` invokes the superclass implementation of an overridden
  method.
- `virtual` methods may be overridden by subclasses using `override`.
  `abstract` methods *must* be overridden by every concrete subclass.

### 7.3 Static Members

```
class Widget {
    private static int instanceCount = 0;
    public static string category = "tool";

    public new() {
        Widget.instanceCount += 1;
    }

    public static function int count() {
        return Widget.instanceCount;
    }
}
```

`static` fields and methods belong to the class itself (a single shared
slot/implementation), and are accessed as `ClassName.member` — including
through a subclass name (`Subclass.staticMember` resolves up the inheritance
chain). They are never accessed through an instance. There is no `static
class`.

### 7.4 Generic Classes

```
class Box<T> {
    public T value;

    public new(T value) {
        this.value = value;
    }

    public function T getValue() {
        return this.value;
    }
}

var intBox = new Box<int>(5);   // type argument is required
```

See §2.5 for the enforcement rules.

### 7.5 Interfaces

```
interfaceDecl = [ "export" ] "interface" identifier
                [ "<" typeParamList ">" ]
                [ "extends" identifier { "," identifier } ]
                "{" { interfaceMethod } "}" ;

interfaceMethod = [ "<" typeParamList ">" ] type identifier
                  "(" [ paramList ] ")" ";" ;
```

```
interface IShape {
    float area();
    string describe();
}

class Circle implements IShape {
    private float radius;

    public new(float radius) { this.radius = radius; }

    public function float area() { return 3.14159 * this.radius * this.radius; }
    public function string describe() { return "circle"; }
}
```

A class `implements` an interface if it (or any of its superclasses)
provides a method matching each interface method's name and parameter count.
`value is IShape` / `value as IShape` check this relationship, including
through interface-to-interface `extends` chains.

### 7.6 Enums

```
enumDecl = [ "export" ] "enum" identifier "{"
             enumMember { "," enumMember } [ "," ]
           "}" ;

enumMember = identifier [ "=" [ "-" ] integerLiteral ] ;
```

```
enum Color {
    Red,
    Green,
    Blue = 10,
    Cyan
}
```

- Members default to sequential `int`/`long` values starting at `0`; an
  explicit `= N` (optionally negative) resets the counter, and subsequent
  members continue counting up from there (so `Cyan` above is `11`).
- Members are accessed as `EnumName.Member` (e.g. `Color.Red`), are usable as
  `switch`/`case` labels and with `==`/`!=`, and `value is EnumName` checks
  whether `value` is a member of that enum.
- A trailing comma after the last member is allowed.

---

## 8. Async / Await

ALKScript scripts are single-threaded and cannot create a deferred operation
themselves — the only source of one is a `native` declaration provided by the
embedding host. Script code can hold, forward, or `await` such a value, but
never constructs one directly.

- `thunk`/`thunk<T>` is a reserved type name representing a not-yet-completed
  deferred operation. It is a real, writable type — usable anywhere a type is
  expected (variable declarations, parameters, return types, `lambda<...>`
  type arguments) — but it is *type-erased*: `thunk<int>` and `thunk<string>`
  are both just "a thunk", and the type argument is not checked.
- Only `native` function/method declarations may declare a `thunk`/`thunk<T>`
  return type — that's the host's signal that calling the function returns a
  deferred value immediately rather than blocking. Internally this is
  represented by `ThunkValue`/`PendingOperationValue` (both reporting
  `TypeName == "thunk"`), wrapping a `Task<ALKScriptValue>`.
- `await expr` suspends until the wrapped operation settles and yields its
  result, unwrapping `thunk<T>` to `T`. `await` is a universally-valid prefix
  operator and may be used in the body of any function, method, or lambda —
  including the entry module's top level (§4.3) — with no declaration-level
  marker required.
- `await` on an expression that does not evaluate to a `thunk`/`thunk<T>`
  value is a no-op: it yields the value unchanged. This is intentionally
  lenient, since a function written to `await` a `thunk<T>` parameter/return
  may also be called with a plain, already-resolved `T`.
- `await [expr1, expr2, ...]` — when the operand of `await` is an array
  literal (or array value) of `thunk`-shaped values, all of them are awaited
  concurrently (`Task.WhenAll` semantics): the result is an array of each
  individual result, and if any of them faults the awaited expression
  propagates that failure.
- A script function/method/lambda that needs to unwrap a `thunk<T>` it
  received (e.g. from a native call) simply `await`s it inline and returns
  the plain unwrapped value — there is no special declaration form for this.
  A function may also *forward* a `thunk<T>` it received by declaring its own
  return type as `thunk<T>` and `return`ing the value without awaiting it.
- A `native` function/method declared `thunk`/`thunk<T>`-returning that is
  called without `await` is **not** started eagerly (lazy/deferred-start). If
  the script ends without ever awaiting it, the host's binder receives a
  `Discard` call so it can run the operation as a fire-and-forget effect.

```
native function thunk<int> delayValue(int value);

function int fetchData() {
    int value = await delayValue(5);
    return value * 2;
}

int doubled = fetchData();
```

```
var endpoints = ["api/users", "api/posts", "api/comments"];
for (var i = 0; i < 3; i = i + 1) {
    var response = await client.get(endpoints[i]);
    log(response);
}
```

---

## 9. Modules

```
importDecl = "import" importClause "from" stringLiteral ";" ;

importClause = "{" importSpecifier { "," importSpecifier } "}"
              | "*" "as" identifier ;

importSpecifier = identifier [ "as" identifier ] ;

exportDecl   = "export" ( classDecl | interfaceDecl | enumDecl
                         | functionDecl | variableDecl ) ;

reExportDecl = "export" "{" importSpecifier { "," importSpecifier } "}"
               "from" stringLiteral ";" ;
```

```
import { log, delayValue } from "console";
import * as Net from "network";

export function int add(int a, int b) { return a + b; }

export { Animal, Dog as Doggo } from "./animals";
```

- `import { A, B as C } from "path"` brings `A` and `C` (an alias for `B`)
  into scope.
- `import * as N from "path"` brings the whole module in as a namespace
  object `N` (members accessed as `N.member`).
- `export` may prefix a class, interface, enum, function, or variable
  declaration.
- `export { A, B as Baz } from "path"` re-exports names from another module
  without binding them locally.
- All `import` declarations must precede every other declaration in a module.
- Module specifiers (`"console"`, `"./animals"`, etc.) are resolved by the
  embedding host — see §10.

---

## 10. Host-Provided Modules ("Standard Library")

ALKScript itself does **not** ship a built-in standard library. The
interpreter resolves `import ... from "<specifier>"` via an
`ICoreModuleProvider`/module-resolution mechanism supplied by the embedding
host; if no provider is registered, *every* import fails with `No core
module '<specifier>' is registered.`

Anything that looks like a "global" — `log`, `delayValue`, `HttpClient`, and
similar names seen throughout the examples in this document and in the test
suite — is a function or class declared `native` (often `export native`) in
a small `.alk` module supplied by the host/test-harness, e.g.:

```
// console.alk, supplied by the host
export native function void log(string message);
export native function thunk<int> delayValue(int value);
```

```
// network.alk, supplied by the host
export native class HttpClient {
    public native function thunk<string> get(string url);
}
```

There is **no** built-in `Error` type, exception hierarchy, `Array<T>`,
`Date`, `print`, `parseInt`, or similar globals available without an explicit
`import`. `throw` accepts a value of any type (commonly a `string`), and
`catch (Type name)` may bind it with any type, or omit the binding entirely
(`catch { ... }`).

Embedding hosts are free to define richer modules (collections, I/O,
networking, etc.); consult the host's documentation/registered modules for
what is actually available in a given runtime environment.

---

## 11. Sample Program

```
import { log } from "console";

interface ISpeaker {
    string speak();
}

abstract class Animal implements ISpeaker {
    protected string name;

    public new(string name) {
        this.name = name;
    }

    public abstract function string speak();
}

class Dog extends Animal {
    public new(string name) {
        base(name);
    }

    public override function string speak() {
        return this.name + " says Woof";
    }
}

enum Mood {
    Calm,
    Excited
}

function string greet(ISpeaker speaker, Mood mood) {
    switch (mood) {
        case Mood.Excited:
            return speaker.speak() + "!!!";
        default:
            return speaker.speak() + ".";
    }
}

lambda<void, string> announce = void (string text) => { log(text); };

var fido = new Dog("Fido");
announce(greet(fido, Mood.Excited));

try {
    if (fido is Animal) {
        log("Fido is an Animal");
    }
} catch (string message) {
    log(`error: ${message}`);
} finally {
    log("done");
}

// Output:
// Fido says Woof!!!
// Fido is an Animal
// done
```
