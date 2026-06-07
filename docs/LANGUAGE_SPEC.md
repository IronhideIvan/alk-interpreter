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
class  new  this  base  extends  public  protected  private  virtual  abstract  override
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
| Null    | `null`                    | any nullable type (`T?`) | Absence of a value |

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
| Class     | `IDENTIFIER`      | A user-defined reference type declared with `class` (see [§8](#8-classes)) |

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

### 2.4 Nullable types

Every type `T` is **non-nullable** by default — `null` cannot be assigned to it.
Appending `?` to a type produces its nullable counterpart `T?`, which additionally
permits the value `null`. This applies uniformly to primitives, arrays, `Task`, and
user-defined types:

```
string name = null;        // compile-time error: 'string' is not nullable
string? name = null;       // ok: 'string?' is nullable
int? maybeCount = null;    // ok
int? maybeCount = 5;       // ok: T is assignable to T?

int[]? items = null;       // ok: a nullable array reference
Task<string>? pending = null;
```

A non-nullable `T` is implicitly assignable to `T?`. The reverse — using a `T?`
where a `T` is required — is a compile-time error unless the value has been
proven non-null (for example, by a prior equality check against `null` in an
enclosing `if`/`while` condition); the type checker narrows `T?` to `T` within
such a guarded branch.

### 2.5 Type compatibility and conversions

- `int` values are implicitly converted to `long`, and `int`/`long` values are
  implicitly converted to `float`, where the wider type is expected (widening
  conversions: `int` → `long` → `float`). The reverse requires an explicit conversion.
- No other implicit conversions are performed; in particular, `string`, `bool`,
  and the numeric types (`int`, `long`, `float`) are not interchangeable.
- `null` is assignable only to nullable types (`T?`); assigning it to a
  non-nullable type, including `int`, `long`, `float`, and `bool`, is a
  compile-time error.

## 3. Grammar

The grammar below is given in EBNF-like notation. Terminals are quoted; `?` means
optional, `*` means zero-or-more, `+` means one-or-more, `|` means alternation.

```ebnf
program        = declaration* EOF ;

declaration    = classDecl
               | functionDecl
               | variableDecl
               | statement ;

classDecl      = "abstract"? "class" IDENTIFIER typeParameters?
                 ( "extends" IDENTIFIER ( "<" type ( "," type )* ">" )? )?
                 "{" member* "}" ;

typeParameters = "<" IDENTIFIER ( "," IDENTIFIER )* ">" ;

member         = constructorDecl
               | fieldDecl
               | methodDecl ;

accessModifier = "public" | "protected" | "private" ;
                 (* defaults to "private" when omitted *)

constructorDecl = accessModifier? "new" "(" parameters? ")" block ;

fieldDecl      = accessModifier? ( "var" | type ) IDENTIFIER ( "=" expression )? ";" ;

methodDecl     = accessModifier? overrideModifier? "async"? "function" typeParameters?
                 type IDENTIFIER "(" parameters? ")" ( block | ";" ) ;
                 (* the body is replaced by ";" only for "abstract" methods *)

overrideModifier = "virtual" | "abstract" | "override" ;

functionDecl   = "async"? "function" typeParameters? type IDENTIFIER
                 "(" parameters? ")" block ;
                 (* an "async" function's declared return type must be "Task"
                    or "Task<T>" *)
parameters     = parameter ( "," parameter )* ;
parameter      = type IDENTIFIER ;

variableDecl   = ( "var" | type ) IDENTIFIER ( "=" expression )? ";" ;
                 (* "var" requires an initializer, from which the type is
                    inferred; an explicit type makes the initializer optional *)

type           = ( "int" | "long" | "float" | "string" | "bool" | "void" | IDENTIFIER )
                 ( "<" type ( "," type )* ">" )?
                 ( "[" "]" )*
                 "?"? ;
                 (* trailing "?" marks the type as nullable, e.g. "int?", "string[]?" *)

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
               | "this" | "base"
               | "new" IDENTIFIER "(" arguments? ")"
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

#### Generic functions

A function may declare one or more **type parameters** in angle brackets between
the `function` keyword and its return type. The type parameters can then be used
as types anywhere within the parameter list, return type, and body:

```
function<T> void process(T n) {
  // Do something with a value of type T
}

function<T> T first(T[] items) {
  return items[0];
}
```

Each call site supplies (or lets the compiler infer from the arguments) a
concrete type for every type parameter; the function is type-checked as if `T`
were that concrete type. `process(5)` instantiates `T` as `int`; `process("hi")`
instantiates it as `string`. Supplying incompatible types for the same type
parameter across a single call (e.g. mismatched array element types) is a
compile-time error.

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
- Equality operators (`== !=`) require both operands to have the same type, or
  one operand to be `null` and the other a nullable type (`T?`); the result is a
  `bool`. Comparing `null` against a non-nullable type is a compile-time error.
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

## 8. Classes

ALKScript supports simple, single-inheritance classes, similar to C#. A class is
a user-defined reference type that groups fields, a constructor, and methods.

### 8.1 Declaring a class

```
class Person {
  protected string name;
  private int age;

  public new(string name, int age) {
    this.name = name;
    this.age = age;
  }

  public virtual function string greet() {
    return "Hello, my name is " + this.name;
  }
}
```

- **Fields** are declared the same way as variables (`type IDENTIFIER` or `var`
  with an initializer), optionally preceded by an access modifier.
- The **constructor** is declared with the `new` keyword in place of a name and
  has no return type; it initializes the instance's fields.
- **Methods** are declared the same way as top-level functions (including the
  `async` modifier), optionally preceded by an access modifier and an
  overridability modifier (`virtual`/`abstract`, see [§8.6](#86-virtual-and-abstract-methods)).
  The `function` keyword is required for every member function — only the
  constructor uses `new` in its place.

### 8.2 Access modifiers

Members may be marked `public`, `protected`, or `private`:

- `public` members are visible to any code that has a reference to the class or
  an instance of it.
- `protected` members are visible from within the class's own members and from
  within the members of any class that (directly or indirectly) `extends` it,
  but not from outside the class hierarchy.
- `private` members are visible only from within the class's own members — not
  even from a derived class.
- A member with no access modifier is `private` by default.

### 8.3 `this` and member access

Within a constructor or method body, `this` refers to the current instance.
Members are accessed with `.`, the same operator used for member access on any
reference type:

```
var p = new Person("Ada", 36);
print(p.greet());
```

`this` is required to disambiguate a field from a parameter or local variable of
the same name (as in the constructor above), and may be omitted otherwise.

### 8.4 Instantiation

Instances are created with `new ClassName(arguments)`, which allocates the
instance and runs its constructor:

```
var p = new Person("Ada", 36);
Person? maybeNobody = null;
```

A class is always a reference type: variables of a class type hold a reference to
an instance (or `null`, if the type is the nullable form `ClassName?`), not the
instance's data directly.

### 8.5 Inheritance

A class may extend exactly one other class with `extends`, inheriting its
`public` and `protected` members. Its `private` members exist on every instance
but cannot be referenced from the derived class — note that `Person.name`
(§8.1) is declared `protected` specifically so that `Employee` can use it
directly, as shown below:

```
class Employee extends Person {
  private string title;

  public new(string name, int age, string title) {
    base(name, age);
    this.title = title;
  }

  public override function string greet() {
    return base.greet() + ", I work as a " + this.title;
  }
}
```

- `base(arguments)` calls the parent class's constructor and must be the first
  statement in a derived class's constructor (if the parent has no
  zero-argument constructor).
- `base.member` accesses a member of the parent class, most commonly to call an
  overridden method's parent implementation.
- A method in a derived class may **override** a method in its parent only if
  the parent's method is declared `virtual` or `abstract` — see [§8.6](#86-virtual-and-abstract-methods).

### 8.6 Virtual and abstract methods

By default, methods cannot be overridden. A method must be explicitly marked
`virtual` or `abstract` in its declaring class to allow a derived class to
provide its own implementation — and the derived class, in turn, must mark its
replacement `override`:

```
class Person {
  // ...

  public virtual function string greet() {
    return "Hello, my name is " + this.name;
  }
}

class Contractor extends Person {
  // ...

  public override function string greet() {
    return base.greet() + ", working as a contractor";
  }
}
```

- A `virtual` method has a body and provides a default implementation that
  derived classes may override.
- An `abstract` method has **no body** — its declaration ends with `;` instead of
  a `block` — and must be overridden by every concrete (non-abstract) derived
  class:

  ```
  public abstract function string greet();
  ```

- A derived-class method that replaces a `virtual` or `abstract` parent method
  (same name and parameter types) **must** be marked `override`. Declaring a
  method with the same signature as a `virtual`/`abstract` parent method without
  `override` — or marking a method `override` when the parent has no matching
  `virtual`/`abstract` method — is a compile-time error. Likewise, overriding a
  method that is neither `virtual` nor `abstract` in its parent is an error.
- An `override` method is itself overridable by further-derived classes, using
  the same `override` keyword (there is no need to repeat `virtual`).

### 8.7 Abstract classes

A class must be declared `abstract` if it declares any `abstract` methods, or if
it inherits one or more `abstract` methods without providing concrete `override`
implementations for all of them:

```
abstract class Person {
  protected string name;

  public new(string name) {
    this.name = name;
  }

  public abstract function string greet();
}
```

- `abstract` is written immediately before `class`, as in `abstract class Person`.
- An abstract class cannot be instantiated with `new` — only a *concrete* class
  (one with no unimplemented `abstract` methods) can be.
- A concrete class extending an abstract class must override every `abstract`
  method it inherits that isn't already overridden by an intermediate class; if
  it does not, it must itself be declared `abstract`.

### 8.8 Generic classes

A class may declare one or more type parameters in angle brackets after its
name. The type parameters can be used as types anywhere within the class —
field types, parameter types, return types, and the bodies of its members:

```
class Array<T> {
  private T[] items = [];

  public void add(T item) {
    // Do something
  }

  public T get(int index) {
    return this.items[index];
  }
}
```

A concrete type is supplied for each type parameter where the class is used —
either explicitly, as in a variable's type or a `new` expression, or (for
top-level usages) inferred from context:

```
var numbers = new Array<int>();
numbers.add(5);

Array<string>? names = null;
```

A generic class may also extend another generic class or interface-like base,
supplying concrete types or its own type parameters for the base's type
parameters, e.g. `class StringArray extends Array<string> { ... }` or
`class Pair<T> extends Container<T> { ... }`.

## 9. Sample Program

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
