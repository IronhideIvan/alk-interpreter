# ALKScript Language Specification

ALKScript is a small, C-like, general-purpose, **strongly and statically typed**
language. This document describes its lexical structure, syntax, and semantics.

## 1. Lexical Structure

### 1.1 Comments

```
// line comment

/* block
   comment */
```

### 1.2 Identifiers

An identifier starts with a letter or underscore, followed by any number of letters,
digits, or underscores: `[a-zA-Z_][a-zA-Z0-9_]*`.

### 1.3 Keywords

Reserved words that cannot be used as identifiers:

```
if  else  while  for  function  return  var  true  false  null
int  long  float  string  bool  void
async  await
```

The last group (`int`, `long`, `float`, `string`, `bool`, `void`) are the built-in
**type names**, used in type annotations.

### 1.4 Literals

| Kind    | Examples                  | Type     | Notes                              |
|---------|---------------------------|----------|------------------------------------|
| Integer | `42`, `0`, `-7`           | `int`    | No decimal point; no exponent form |
| Long    | `42L`, `42l`              | `long`   | Integer literal with an `L`/`l` suffix |
| Float   | `3.14`, `0.5`             | `float`  | Contains a decimal point           |
| String  | `"hello"`, `"line\n"`     | `string` | Double-quoted, escape sequences    |
| Boolean | `true`, `false`           | `bool`   | Keyword literals                   |
| Null    | `null`                    | any reference type | Absence of a value       |

The lexer does not distinguish `int` from `float` literals (aside from the `L`/`l`
suffix that marks a `long`) — these produce a `Number` token, and the type checker
determines `int` vs. `float` based on whether the lexeme contains a decimal point.

### 1.5 Operators and Punctuation

```
+  -  *  /  %
=  ==  !  !=  <  <=  >  >=  &&  ||
(  )  {  }  [  ]
,  ;  :  .
```

## 2. Types

ALKScript is **strongly and statically typed**. Every variable, function parameter,
and function return value has a type that is known at compile time and checked
before the program runs. There is no implicit conversion between unrelated types
and no dynamic re-typing of variables.

### 2.1 Primitive types

| Type     | Description                                  |
|----------|----------------------------------------------|
| `int`    | 32-bit signed integer                        |
| `long`   | 64-bit signed integer                        |
| `float`  | Double-precision floating point              |
| `string` | Sequence of characters                       |
| `bool`   | `true` or `false`                            |
| `void`   | No value; only valid as a function return type |

### 2.2 Composite types

| Type      | Syntax            | Description                                         |
|-----------|-------------------|-----------------------------------------------------|
| Array     | `T[]`             | An ordered, indexable collection of values of type `T` |
| Function  | `(T1, T2) -> R`   | A callable value taking parameters of types `T1, T2` and returning `R` |
| Task      | `Task` / `Task<T>` | Represents an asynchronous operation; `Task<T>` represents one that produces a value of type `T` when it completes |

### 2.3 Variable declarations and type inference

A variable is declared either with an explicit type or with `var`:

```
int num = 1;
int[] numArr = [1, 2, 3, 4];
var num = 1;        // inferred as int
```

When `var` is used, the variable must have an initializer, and its type is
**inferred** from the initializer expression:

```
var x = 10;         // inferred as int
var pi = 3.14;      // inferred as float
```

Once a variable's type is fixed — whether explicitly declared or inferred via
`var` — it cannot change. Assigning a value of a different, incompatible type is
a compile-time error. `var` is purely a declaration-site convenience; it does not
make the variable dynamically typed.

### 2.4 Type compatibility and conversions

- `int` values are implicitly converted to `long`, and `int`/`long` values are
  implicitly converted to `float`, where the wider type is expected (widening
  conversions: `int` → `long` → `float`). The reverse requires an explicit conversion.
- No other implicit conversions are performed; in particular, `string`, `bool`,
  and the numeric types (`int`, `long`, `float`) are not interchangeable.
- `null` is assignable to any reference type but not to `int`, `long`, `float`, or `bool`.

## 3. Grammar

The grammar below is given in EBNF-like notation. Terminals are quoted; `?` means
optional, `*` means zero-or-more, `+` means one-or-more, `|` means alternation.

```ebnf
program        = declaration* EOF ;

declaration    = functionDecl
               | variableDecl
               | statement ;

functionDecl   = "async"? "function" type IDENTIFIER "(" parameters? ")" block ;
                 (* an "async" function's declared return type must be "Task"
                    or "Task<T>" *)
parameters     = parameter ( "," parameter )* ;
parameter      = type IDENTIFIER ;

variableDecl   = ( "var" | type ) IDENTIFIER ( "=" expression )? ";" ;
                 (* "var" requires an initializer, from which the type is
                    inferred; an explicit type makes the initializer optional *)

type           = ( "int" | "long" | "float" | "string" | "bool" | "void" | IDENTIFIER )
                 ( "<" type ( "," type )* ">" )?
                 ( "[" "]" )* ;

statement      = exprStatement
               | ifStatement
               | whileStatement
               | forStatement
               | returnStatement
               | block ;

exprStatement  = expression ";" ;

ifStatement    = "if" "(" expression ")" statement ( "else" statement )? ;

whileStatement = "while" "(" expression ")" statement ;

forStatement   = "for" "(" ( variableDecl | exprStatement | ";" )
                           expression? ";"
                           expression? ")" statement ;

returnStatement = "return" expression? ";" ;

block          = "{" declaration* "}" ;

expression     = assignment ;

assignment     = IDENTIFIER "=" assignment
               | logicOr ;

logicOr        = logicAnd ( "||" logicAnd )* ;
logicAnd       = equality ( "&&" equality )* ;
equality       = comparison ( ( "==" | "!=" ) comparison )* ;
comparison     = term ( ( "<" | "<=" | ">" | ">=" ) term )* ;
term           = factor ( ( "+" | "-" ) factor )* ;
factor         = unary ( ( "*" | "/" | "%" ) unary )* ;
unary          = ( "!" | "-" | "await" ) unary
               | call ;
call           = primary ( "(" arguments? ")" | "." IDENTIFIER | "[" expression "]" )* ;
arguments      = expression ( "," expression )* ;

primary        = NUMBER | STRING | "true" | "false" | "null"
               | IDENTIFIER
               | "(" expression ")"
               | "[" arguments? "]" ;
```

## 4. Operator Precedence

From lowest to highest precedence:

1. Assignment (`=`)
2. Logical OR (`||`)
3. Logical AND (`&&`)
4. Equality (`==`, `!=`)
5. Comparison (`<`, `<=`, `>`, `>=`)
6. Addition / subtraction (`+`, `-`)
7. Multiplication / division / modulo (`*`, `/`, `%`)
8. Unary (`!`, `-`, `await`)
9. Call / index / member access (`()`, `[]`, `.`)

All binary operators are left-associative. Assignment is right-associative.

## 5. Statements and Declarations

### 5.1 Variable declaration

A variable is declared with either an explicit type or `var`, similar to C#:

```
int num = 1;
int[] numArr = [1, 2, 3, 4];
var num = 1;            // inferred as int
string name;            // declared without an initializer; defaults to null
```

`var` always requires an initializer (the type is inferred from it); an explicit
type makes the initializer optional.

### 5.2 Function declaration

The return type immediately follows the `function` keyword (use `void` if the
function returns nothing), and each parameter declares its type before its name —
the same `type IDENTIFIER` pattern used for variable declarations:

```
function int add(int a, int b) {
  return a + b;
}

function void log(string message) {
  print(message);
}
```

### 5.3 Control flow

```
if (x > 0) {
  // ...
} else {
  // ...
}

while (x < 10) {
  x = x + 1;
}

for (var i = 0; i < 10; i = i + 1) {
  // ...
}
```

### 5.4 Blocks and scope

A block introduces a new lexical scope. Variables are visible from their
declaration point to the end of the enclosing block.

## 6. Expressions

- Arithmetic operators (`+ - * / %`) require both operands to be numeric
  (`int`, `long`, or `float`); the narrower operand is widened to match the wider
  one (`int` → `long` → `float`), and the result has the wider of the two types.
  `+` also concatenates two `string` operands, producing a `string`.
- Comparison operators (`< <= > >=`) require both operands to be numeric
  (`int`, `long`, or `float`) and produce a `bool`.
- Equality operators (`== !=`) require both operands to have the same type (or one
  to be `null` for a reference type) and produce a `bool`.
- Logical operators (`&& ||`) require both operands to be `bool` and short-circuit,
  producing a `bool`.
- The unary `!` requires a `bool` operand; unary `-` requires a numeric
  (`int`, `long`, or `float`) operand and preserves its type.
- Mixing incompatible types in any of the above is a compile-time type error —
  there is no implicit conversion between, for example, `string` and `int`.

## 7. Asynchronous Functions (`async`/`await`)

ALKScript supports an `async`/`await` pattern for asynchronous operations,
modeled on C#.

### 7.1 Declaring an async function

A function is marked asynchronous by prefixing its declaration with `async`.
An `async` function's declared return type must be `Task` (for a function that
produces no value) or `Task<T>` (for a function that produces a value of type `T`):

```
async function Task<string> fetchGreeting(string name) {
  var message = await buildGreeting(name);
  return message;
}

async function Task logMessage(string message) {
  await writeToLog(message);
}
```

Inside the body of an `async` function, `return expr;` where the declared return
type is `Task<T>` returns a value of type `T` — the runtime wraps it in the
`Task<T>` that the function as a whole produces. In a `Task`-returning `async`
function, `return;` (or falling off the end of the body) completes the `Task`.

### 7.2 The `await` expression

`await` is a unary, prefix operator that suspends execution until the awaited
operation completes:

```
await expression
```

- The operand must have type `Task` or `Task<T>`.
- Awaiting a `Task<T>` yields a value of type `T`.
- Awaiting a `Task` yields no value (its type is `void`); the result cannot be
  used in a context that requires a value.
- `await` is only valid inside the body of an `async` function. Using it
  elsewhere is a compile-time error.

### 7.3 Calling async functions

Calling an `async` function does not block — it immediately returns a `Task` or
`Task<T>` representing the in-progress operation. The caller can either `await`
that result (suspending itself, and therefore must itself be `async`) or store
and use it later:

```
async function Task<int> run() {
  var greetingTask = fetchGreeting("ALKScript");  // starts running, doesn't block
  var greeting = await greetingTask;              // suspend until it completes
  print(greeting);
  return greeting.length;
}
```

## 8. Sample Program

```
function int fibonacci(int n) {
  if (n <= 1) {
    return n;
  }

  return fibonacci(n - 1) + fibonacci(n - 2);
}

int i = 0;
while (i < 10) {
  print(fibonacci(i));
  i = i + 1;
}
```
