using System;
using System.Collections.Generic;
using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Common.Modules;
using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Evaluator.Cursor
{
  /// <summary>
  /// Two-way mapping between <see cref="AstReference"/>s and the
  /// declarations they address within a <see cref="ModuleGraph"/> — the
  /// "Phase B" structural Capture/Restore design's AST-addressing scheme
  /// (docs/ASYNC_AWAIT_DESIGN.md Addendum 3).
  ///
  /// <see cref="BuildAddressTable"/> is the Capture-side direction: given a
  /// declaration object encountered in the heap (e.g.
  /// <c>classValue.Declaration</c>), look up its <see cref="AstReference"/>
  /// by reference identity. <see cref="Resolve"/> is the Restore-side
  /// direction: given an <see cref="AstReference"/>, find the declaration in
  /// a freshly-loaded, equivalent <see cref="ModuleGraph"/>.
  ///
  /// Covers top-level <see cref="ClassDecl"/>/<see cref="InterfaceDecl"/>/
  /// <see cref="EnumDecl"/>/<see cref="FunctionDecl"/> declarations (unwrapping
  /// <see cref="ExportDecl"/>). Class members, enum members, and lambdas are
  /// addressed via dotted <see cref="AstReference.Path"/> segments handled by
  /// <see cref="Resolve"/>.
  /// </summary>
  public static class AstResolver
  {
    /// <summary>
    /// Builds a Capture-side lookup from top-level declaration objects (by
    /// reference identity) to the <see cref="AstReference"/> that addresses
    /// them.
    /// </summary>
    public static Dictionary<object, AstReference> BuildAddressTable(ModuleGraph graph)
    {
      var table = new Dictionary<object, AstReference>(ReferenceEqualityComparer<object>.Instance);

      for (int i = 0; i < graph.GlobalPreludes.Count; i++)
      {
        IndexProgram(table, graph.GlobalPreludes[i], AstReference.ForPrelude(i));
      }

      foreach (var module in graph.Modules.Values)
      {
        IndexProgram(table, module.Program, AstReference.ForModule(module.Identifier));
      }

      return table;
    }

    private static void IndexProgram(Dictionary<object, AstReference> table, ProgramNode program, string moduleKey)
    {
      foreach (var stmt in program.Declarations)
      {
        switch (UnwrapExport(stmt))
        {
          case ClassDecl classDecl:
            table[classDecl] = new AstReference(moduleKey, classDecl.Name.Lexeme);
            break;

          case InterfaceDecl interfaceDecl:
            table[interfaceDecl] = new AstReference(moduleKey, interfaceDecl.Name.Lexeme);
            break;

          case EnumDecl enumDecl:
            table[enumDecl] = new AstReference(moduleKey, enumDecl.Name.Lexeme);
            break;

          case FunctionDecl functionDecl:
            table[functionDecl] = new AstReference(moduleKey, functionDecl.Name.Lexeme);
            break;
        }
      }
    }

    /// <summary>
    /// Resolves an <see cref="AstReference"/> to the declaration it
    /// addresses within <paramref name="graph"/>. For a top-level reference
    /// (no "." in <see cref="AstReference.Path"/>) this is a
    /// <see cref="ClassDecl"/>/<see cref="InterfaceDecl"/>/<see cref="EnumDecl"/>/
    /// <see cref="FunctionDecl"/>.
    /// </summary>
    public static Decl ResolveTopLevel(ModuleGraph graph, AstReference reference)
    {
      var program = GetProgram(graph, reference.ModuleKey);
      var segments = reference.Path.Split('.');

      foreach (var stmt in program.Declarations)
      {
        var decl = UnwrapExport(stmt);

        if (decl != null && NameOf(decl) == segments[0])
        {
          return decl;
        }
      }

      throw new KeyNotFoundException(
        $"AstReference '{reference}' does not resolve: no top-level declaration named '{segments[0]}' in module '{reference.ModuleKey}'.");
    }

    /// <summary>The path segment addressing a class's single constructor.</summary>
    public const string ConstructorSegment = "<ctor>";

    /// <summary>The path-segment prefix addressing a lambda by source position, followed by "{line}:{column}".</summary>
    public const string LambdaPrefix = "<lambda>@";

    /// <summary>
    /// Resolves any <see cref="AstReference"/> — top-level or with dotted
    /// member segments — to the object it addresses: a top-level
    /// <see cref="Decl"/>, a <see cref="MemberDecl"/> (<see cref="MethodDecl"/>/
    /// <see cref="FieldDecl"/>/<see cref="ConstructorDecl"/>), an
    /// <see cref="EnumMember"/>, or a <see cref="LambdaExpr"/>.
    /// </summary>
    public static object Resolve(ModuleGraph graph, AstReference reference)
    {
      var segments = reference.Path.Split('.');

      object current;
      if (segments[0].StartsWith(LambdaPrefix, StringComparison.Ordinal))
      {
        var program = GetProgram(graph, reference.ModuleKey);
        var (line, column) = ParseLambdaSegment(segments[0], reference);
        current = FindLambda(AstLambdaFinder.FindInStatements(program.Declarations, line, column), segments[0], reference);
      }
      else
      {
        current = ResolveTopLevel(graph, new AstReference(reference.ModuleKey, segments[0]));
      }

      for (int i = 1; i < segments.Length; i++)
      {
        current = ResolveMember(current, segments[i], reference);
      }

      return current;
    }

    private static LambdaExpr FindLambda(LambdaExpr? found, string segment, AstReference reference) =>
      found ?? throw new KeyNotFoundException($"AstReference '{reference}' does not resolve: no lambda found at position '{segment}'.");

    /// <summary>
    /// Searches the body of a member/lambda for a nested <see cref="LambdaExpr"/>
    /// at the given source position. <see cref="FieldDecl"/>'s body is an
    /// initializer <see cref="Expr"/>; everything else has a <see cref="BlockStmt"/> body.
    /// </summary>
    private static LambdaExpr? FindLambdaInBody(object parent, int line, int column, AstReference reference) => parent switch
    {
      MethodDecl method => AstLambdaFinder.FindInStmt(method.Body, line, column),
      FunctionDecl functionDecl => AstLambdaFinder.FindInStmt(functionDecl.Body, line, column),
      ConstructorDecl ctor => AstLambdaFinder.FindInStmt(ctor.Body, line, column),
      FieldDecl field => AstLambdaFinder.FindInExpr(field.Initializer, line, column),
      LambdaExpr lambda => AstLambdaFinder.FindInStmt(lambda.Body, line, column),
      _ => throw new NotSupportedException(
        $"AstReference '{reference}' does not resolve: '{parent.GetType().Name}' has no searchable body for a nested lambda."),
    };

    private static (int Line, int Column) ParseLambdaSegment(string segment, AstReference reference)
    {
      string position = segment.Substring(LambdaPrefix.Length);
      int colonIndex = position.IndexOf(':');

      if (colonIndex < 0
        || !int.TryParse(position.Substring(0, colonIndex), out int line)
        || !int.TryParse(position.Substring(colonIndex + 1), out int column))
      {
        throw new FormatException($"AstReference '{reference}' has a malformed lambda segment '{segment}'.");
      }

      return (line, column);
    }

    private static object ResolveMember(object parent, string segment, AstReference reference)
    {
      if (segment.StartsWith(LambdaPrefix, StringComparison.Ordinal))
      {
        var (line, column) = ParseLambdaSegment(segment, reference);
        return FindLambda(FindLambdaInBody(parent, line, column, reference), segment, reference);
      }

      switch (parent)
      {
        case ClassDecl classDecl:
          if (segment == ConstructorSegment)
          {
            foreach (var member in classDecl.Members)
            {
              if (member is ConstructorDecl ctor)
              {
                return ctor;
              }
            }

            throw new KeyNotFoundException(
              $"AstReference '{reference}' does not resolve: class '{classDecl.Name.Lexeme}' has no constructor.");
          }

          foreach (var member in classDecl.Members)
          {
            switch (member)
            {
              case MethodDecl method when method.Name.Lexeme == segment:
                return method;

              case FieldDecl field when field.Name.Lexeme == segment:
                return field;
            }
          }

          throw new KeyNotFoundException(
            $"AstReference '{reference}' does not resolve: class '{classDecl.Name.Lexeme}' has no member named '{segment}'.");

        case EnumDecl enumDecl:
          foreach (var member in enumDecl.Members)
          {
            if (member.Name.Lexeme == segment)
            {
              return member;
            }
          }

          throw new KeyNotFoundException(
            $"AstReference '{reference}' does not resolve: enum '{enumDecl.Name.Lexeme}' has no member named '{segment}'.");

        default:
          throw new NotSupportedException(
            $"AstReference '{reference}' does not resolve: '{parent.GetType().Name}' has no addressable member named '{segment}'.");
      }
    }

    /// <summary>
    /// Addresses a member of the class referenced by <paramref name="classRef"/>
    /// — a method/field by <paramref name="memberName"/>, or the class's
    /// constructor via <see cref="ConstructorSegment"/>.
    /// </summary>
    public static AstReference AddressOfMember(AstReference classRef, string memberName) =>
      new AstReference(classRef.ModuleKey, classRef.Path + "." + memberName);

    /// <summary>
    /// Addresses a lambda nested within the declaration referenced by
    /// <paramref name="enclosingRef"/>, by the source position of its
    /// <c>=&gt;</c> token.
    /// </summary>
    public static AstReference AddressOfLambda(AstReference enclosingRef, ALKScriptToken arrow) =>
      new AstReference(enclosingRef.ModuleKey, enclosingRef.Path + "." + LambdaPrefix + arrow.Line + ":" + arrow.Column);

    private static string? NameOf(Decl decl) => decl switch
    {
      ClassDecl classDecl => classDecl.Name.Lexeme,
      InterfaceDecl interfaceDecl => interfaceDecl.Name.Lexeme,
      EnumDecl enumDecl => enumDecl.Name.Lexeme,
      FunctionDecl functionDecl => functionDecl.Name.Lexeme,
      _ => null,
    };

    internal static ProgramNode GetProgram(ModuleGraph graph, string moduleKey)
    {
      if (moduleKey.StartsWith("prelude:", StringComparison.Ordinal))
      {
        int index = int.Parse(moduleKey.Substring("prelude:".Length));
        return graph.GlobalPreludes[index];
      }

      if (moduleKey.StartsWith("module:", StringComparison.Ordinal))
      {
        string identifier = moduleKey.Substring("module:".Length);
        return graph.Modules[identifier].Program;
      }

      throw new FormatException($"Unrecognized AstReference module key '{moduleKey}'.");
    }

    private static Decl? UnwrapExport(Stmt stmt) => stmt switch
    {
      ExportDecl exportDecl => exportDecl.Declaration,
      Decl decl => decl,
      _ => null,
    };
  }
}
