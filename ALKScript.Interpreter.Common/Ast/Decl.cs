using System.Collections.Generic;
using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>
  /// Base type for all top-level/member declaration AST nodes. Corresponds to
  /// the "declaration" production in the language grammar (§3).
  /// </summary>
  public abstract class Decl : Stmt
  {
  }

  /// <summary>
  /// Wraps a plain statement so it can appear in a "declaration*" list. The
  /// grammar's "declaration" production permits ordinary statements (e.g.
  /// expression statements, control flow) alongside class/function/variable
  /// declarations; this node lets the parser represent that list uniformly
  /// as <see cref="Decl"/>/<see cref="Stmt"/> values without losing the
  /// distinction between a "real" declaration and a wrapped statement.
  /// </summary>
  public class StatementDecl : Decl
  {
    public Stmt Statement { get; }

    public StatementDecl(Stmt statement)
    {
      Statement = statement;
    }
  }

  /// <summary>
  /// A variable declaration: ("var" | type) IDENTIFIER ("=" expression)? ";".
  /// "var" requires an initializer (the type is inferred); an explicit type
  /// makes the initializer optional.
  /// </summary>
  public class VariableDecl : Decl
  {
    /// <summary>The declared type, or null when "var" is used (type is inferred).</summary>
    public TypeNode? Type { get; }

    public ALKScriptToken Name { get; }
    public Expr? Initializer { get; }

    public VariableDecl(TypeNode? type, ALKScriptToken name, Expr? initializer)
    {
      Type = type;
      Name = name;
      Initializer = initializer;
    }
  }

  /// <summary>
  /// A top-level function declaration:
  ///   "async"? "function" typeParameters? type IDENTIFIER "(" parameters? ")" block ;
  /// </summary>
  public class FunctionDecl : Decl
  {
    public bool IsAsync { get; }
    public IReadOnlyList<string> TypeParameters { get; }
    public TypeNode ReturnType { get; }
    public ALKScriptToken Name { get; }
    public IReadOnlyList<Parameter> Parameters { get; }
    public BlockStmt Body { get; }

    public FunctionDecl(
      bool isAsync,
      IReadOnlyList<string> typeParameters,
      TypeNode returnType,
      ALKScriptToken name,
      IReadOnlyList<Parameter> parameters,
      BlockStmt body)
    {
      IsAsync = isAsync;
      TypeParameters = typeParameters;
      ReturnType = returnType;
      Name = name;
      Parameters = parameters;
      Body = body;
    }
  }

  /// <summary>The "public" | "protected" | "private" access modifiers (defaults to "private" when omitted).</summary>
  public enum AccessModifier
  {
    Private,
    Protected,
    Public
  }

  /// <summary>The "virtual" | "abstract" | "override" method modifiers.</summary>
  public enum OverrideModifier
  {
    None,
    Virtual,
    Abstract,
    Override
  }

  /// <summary>Base type for class members: constructors, fields, and methods.</summary>
  public abstract class MemberDecl
  {
    public AccessModifier AccessModifier { get; }

    protected MemberDecl(AccessModifier accessModifier)
    {
      AccessModifier = accessModifier;
    }
  }

  /// <summary>
  /// A constructor declaration: accessModifier? "new" "(" parameters? ")" block.
  /// </summary>
  public class ConstructorDecl : MemberDecl
  {
    public IReadOnlyList<Parameter> Parameters { get; }
    public BlockStmt Body { get; }

    public ConstructorDecl(AccessModifier accessModifier, IReadOnlyList<Parameter> parameters, BlockStmt body)
      : base(accessModifier)
    {
      Parameters = parameters;
      Body = body;
    }
  }

  /// <summary>
  /// A field declaration: accessModifier? ("var" | type) IDENTIFIER ("=" expression)? ";".
  /// </summary>
  public class FieldDecl : MemberDecl
  {
    /// <summary>The declared type, or null when "var" is used (type is inferred).</summary>
    public TypeNode? Type { get; }

    public ALKScriptToken Name { get; }
    public Expr? Initializer { get; }

    public FieldDecl(AccessModifier accessModifier, TypeNode? type, ALKScriptToken name, Expr? initializer)
      : base(accessModifier)
    {
      Type = type;
      Name = name;
      Initializer = initializer;
    }
  }

  /// <summary>
  /// A method declaration:
  ///   accessModifier? overrideModifier? "async"? "function" typeParameters?
  ///   type IDENTIFIER "(" parameters? ")" ( block | ";" ) ;
  /// The body is null only for "abstract" methods, whose declaration ends with ";".
  /// </summary>
  public class MethodDecl : MemberDecl
  {
    public OverrideModifier OverrideModifier { get; }
    public bool IsAsync { get; }
    public IReadOnlyList<string> TypeParameters { get; }
    public TypeNode ReturnType { get; }
    public ALKScriptToken Name { get; }
    public IReadOnlyList<Parameter> Parameters { get; }
    public BlockStmt? Body { get; }

    public MethodDecl(
      AccessModifier accessModifier,
      OverrideModifier overrideModifier,
      bool isAsync,
      IReadOnlyList<string> typeParameters,
      TypeNode returnType,
      ALKScriptToken name,
      IReadOnlyList<Parameter> parameters,
      BlockStmt? body)
      : base(accessModifier)
    {
      OverrideModifier = overrideModifier;
      IsAsync = isAsync;
      TypeParameters = typeParameters;
      ReturnType = returnType;
      Name = name;
      Parameters = parameters;
      Body = body;
    }
  }

  /// <summary>
  /// A class declaration:
  ///   "abstract"? "class" IDENTIFIER typeParameters?
  ///   ( "extends" IDENTIFIER ( "&lt;" type ("," type)* "&gt;" )? )?
  ///   "{" member* "}" ;
  /// </summary>
  public class ClassDecl : Decl
  {
    public bool IsAbstract { get; }
    public ALKScriptToken Name { get; }
    public IReadOnlyList<string> TypeParameters { get; }
    public ALKScriptToken? SuperclassName { get; }
    public IReadOnlyList<TypeNode> SuperclassTypeArguments { get; }
    public IReadOnlyList<MemberDecl> Members { get; }

    public ClassDecl(
      bool isAbstract,
      ALKScriptToken name,
      IReadOnlyList<string> typeParameters,
      ALKScriptToken? superclassName,
      IReadOnlyList<TypeNode> superclassTypeArguments,
      IReadOnlyList<MemberDecl> members)
    {
      IsAbstract = isAbstract;
      Name = name;
      TypeParameters = typeParameters;
      SuperclassName = superclassName;
      SuperclassTypeArguments = superclassTypeArguments;
      Members = members;
    }
  }

  /// <summary>
  /// An "export" declaration wrapping a class, function, or variable declaration.
  /// Only valid on top-level declarations.
  /// </summary>
  public class ExportDecl : Decl
  {
    public Decl Declaration { get; }

    public ExportDecl(Decl declaration)
    {
      Declaration = declaration;
    }
  }

  /// <summary>A single named import specifier: IDENTIFIER ("as" IDENTIFIER)?.</summary>
  public class ImportSpecifier
  {
    public ALKScriptToken Name { get; }
    public ALKScriptToken? Alias { get; }

    public ImportSpecifier(ALKScriptToken name, ALKScriptToken? alias)
    {
      Name = name;
      Alias = alias;
    }
  }

  /// <summary>Base type for the two import-clause forms: named imports and namespace imports.</summary>
  public abstract class ImportClause
  {
  }

  /// <summary>A named-imports clause: "{" importSpecifier ("," importSpecifier)* "}".</summary>
  public class NamedImportsClause : ImportClause
  {
    public IReadOnlyList<ImportSpecifier> Specifiers { get; }

    public NamedImportsClause(IReadOnlyList<ImportSpecifier> specifiers)
    {
      Specifiers = specifiers;
    }
  }

  /// <summary>A namespace-import clause: "*" "as" IDENTIFIER.</summary>
  public class NamespaceImportClause : ImportClause
  {
    public ALKScriptToken Alias { get; }

    public NamespaceImportClause(ALKScriptToken alias)
    {
      Alias = alias;
    }
  }

  /// <summary>
  /// An import declaration: "import" importClause "from" STRING ";".
  /// Import declarations must precede all other declarations in a module.
  /// </summary>
  public class ImportDecl
  {
    public ImportClause Clause { get; }
    public ALKScriptToken Source { get; }

    public ImportDecl(ImportClause clause, ALKScriptToken source)
    {
      Clause = clause;
      Source = source;
    }
  }
}
