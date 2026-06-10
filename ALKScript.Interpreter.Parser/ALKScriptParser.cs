using System;
using System.Collections.Generic;
using System.Globalization;
using ALKScript.Interpreter.Common;
using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Parser
{
  /// <summary>
  /// A recursive-descent parser that turns the token stream produced by
  /// <see cref="IScriptLexer"/> into an abstract syntax tree, following the
  /// grammar described in §3 of the ALKScript language specification
  /// (docs/LANGUAGE_SPEC.md).
  /// </summary>
  public class ALKScriptParser : IScriptParser
  {
    private static readonly IReadOnlyDictionary<ALKScriptTokenType, string> PrimitiveTypeNames =
      new Dictionary<ALKScriptTokenType, string>
      {
        { ALKScriptTokenType.IntKeyword, "int" },
        { ALKScriptTokenType.LongKeyword, "long" },
        { ALKScriptTokenType.FloatKeyword, "float" },
        { ALKScriptTokenType.StringKeyword, "string" },
        { ALKScriptTokenType.BoolKeyword, "bool" },
        { ALKScriptTokenType.VoidKeyword, "void" },
      };

    private TokenStream _stream = null!;
    private readonly ParsingContext _parsingContext = new ParsingContext();

    /// <summary>Parses the given token stream into a <see cref="ProgramNode"/>.</summary>
    public ProgramNode ParseTokens(IEnumerable<ALKScriptToken> tokens)
    {
      _stream = new TokenStream(tokens);

      var imports = new List<ImportDecl>();

      while (_stream.Check(ALKScriptTokenType.Import))
      {
        imports.Add(ParseImportDecl());
      }

      var declarations = new List<Stmt>();

      // The entry module's top level runs as part of the (Task-returning)
      // overall program evaluation and can itself genuinely suspend on an
      // "await" — mirroring top-level "await" in other async-capable
      // languages — so it counts as an async context for this check.
      using (_parsingContext.EnterFunctionBody(isAsync: true))
      {
        while (!_stream.IsAtEnd())
        {
          declarations.Add(ParseDeclaration());
        }
      }

      return new ProgramNode(imports, declarations);
    }

    #region Imports

    private ImportDecl ParseImportDecl()
    {
      _stream.Consume(ALKScriptTokenType.Import, "Expect 'import'.");

      ImportClause clause = ParseImportClause();

      _stream.Consume(ALKScriptTokenType.From, "Expect 'from' after import clause.");
      ALKScriptToken source = _stream.Consume(ALKScriptTokenType.String, "Expect a module path string after 'from'.");
      _stream.Consume(ALKScriptTokenType.Semicolon, "Expect ';' after import declaration.");

      return new ImportDecl(clause, source);
    }

    private ImportClause ParseImportClause()
    {
      if (_stream.Match(ALKScriptTokenType.Star))
      {
        _stream.Consume(ALKScriptTokenType.As, "Expect 'as' after '*' in a namespace import.");
        ALKScriptToken alias = _stream.Consume(ALKScriptTokenType.Identifier, "Expect a binding name after 'as'.");
        return new NamespaceImportClause(alias);
      }

      _stream.Consume(ALKScriptTokenType.LeftBrace, "Expect '{' to begin a named-imports clause.");

      var specifiers = new List<ImportSpecifier>();

      if (!_stream.Check(ALKScriptTokenType.RightBrace))
      {
        do
        {
          specifiers.Add(ParseImportSpecifier());
        } while (_stream.Match(ALKScriptTokenType.Comma));
      }

      _stream.Consume(ALKScriptTokenType.RightBrace, "Expect '}' after named imports.");

      return new NamedImportsClause(specifiers);
    }

    private ImportSpecifier ParseImportSpecifier()
    {
      ALKScriptToken name = _stream.Consume(ALKScriptTokenType.Identifier, "Expect an import name.");
      ALKScriptToken? alias = null;

      if (_stream.Match(ALKScriptTokenType.As))
      {
        alias = _stream.Consume(ALKScriptTokenType.Identifier, "Expect a binding name after 'as'.");
      }

      return new ImportSpecifier(name, alias);
    }

    #endregion

    #region Declarations

    private Decl ParseDeclaration()
    {
      if (_stream.Match(ALKScriptTokenType.Export))
      {
        return ParseExportDecl();
      }

      if (CheckClassDeclStart())
      {
        return ParseClassDecl();
      }

      if (CheckFunctionDeclStart())
      {
        return ParseFunctionDecl();
      }

      if (TryParseVariableDecl(out VariableDecl? variableDecl))
      {
        return variableDecl!;
      }

      // Per the grammar, "declaration" also permits plain statements at the
      // top level and inside blocks. Wrap any such statement so that callers
      // (which expect a Decl/Stmt list) can treat it uniformly.
      return new StatementDecl(ParseStatement());
    }

    private ExportDecl ParseExportDecl()
    {
      // "export" is only valid immediately before a class, function, or
      // variable declaration.
      if (CheckClassDeclStart())
      {
        return new ExportDecl(ParseClassDecl());
      }

      if (CheckFunctionDeclStart())
      {
        return new ExportDecl(ParseFunctionDecl());
      }

      if (TryParseVariableDecl(out VariableDecl? variableDecl))
      {
        return new ExportDecl(variableDecl!);
      }

      throw Error(_stream.Peek(), "Expect a class, function, or variable declaration after 'export'.");
    }

    private bool CheckClassDeclStart()
    {
      int offset = 0;

      if (_stream.CheckAhead(offset, ALKScriptTokenType.Native))
      {
        offset++;
      }

      if (_stream.CheckAhead(offset, ALKScriptTokenType.Abstract))
      {
        offset++;
      }

      return _stream.CheckAhead(offset, ALKScriptTokenType.Class);
    }

    private ClassDecl ParseClassDecl()
    {
      bool isNative = _stream.Match(ALKScriptTokenType.Native);
      bool isAbstract = _stream.Match(ALKScriptTokenType.Abstract);
      _stream.Consume(ALKScriptTokenType.Class, "Expect 'class'.");

      ALKScriptToken name = _stream.Consume(ALKScriptTokenType.Identifier, "Expect a class name.");
      List<string> typeParameters = ParseOptionalTypeParameters();

      ALKScriptToken? superclassName = null;
      List<TypeNode> superclassTypeArguments = new List<TypeNode>();

      if (_stream.Match(ALKScriptTokenType.Extends))
      {
        superclassName = _stream.Consume(ALKScriptTokenType.Identifier, "Expect a superclass name after 'extends'.");

        if (_stream.Match(ALKScriptTokenType.Less))
        {
          do
          {
            superclassTypeArguments.Add(ParseType());
          } while (_stream.Match(ALKScriptTokenType.Comma));

          _stream.Consume(ALKScriptTokenType.Greater, "Expect '>' after superclass type arguments.");
        }
      }

      _stream.Consume(ALKScriptTokenType.LeftBrace, "Expect '{' before class body.");

      var members = new List<MemberDecl>();

      while (!_stream.Check(ALKScriptTokenType.RightBrace) && !_stream.IsAtEnd())
      {
        members.Add(ParseMember());
      }

      _stream.Consume(ALKScriptTokenType.RightBrace, "Expect '}' after class body.");

      if (!isNative)
      {
        foreach (var member in members)
        {
          if (member is MethodDecl { IsNative: true } nativeMethod)
          {
            throw Error(nativeMethod.Name, $"Class '{name.Lexeme}' must be declared 'native' because it has a native member '{nativeMethod.Name.Lexeme}'.");
          }
        }
      }

      return new ClassDecl(isAbstract, name, typeParameters, superclassName, superclassTypeArguments, members, isNative);
    }

    private MemberDecl ParseMember()
    {
      AccessModifier accessModifier = ParseOptionalAccessModifier();

      // Constructor: accessModifier? "new" "(" parameters? ")" block
      if (_stream.Check(ALKScriptTokenType.New))
      {
        _stream.Advance();
        List<Parameter> ctorParameters = ParseParameterList();
        BlockStmt ctorBody;
        using (_parsingContext.EnterFunctionBody(isAsync: false))
        {
          ctorBody = ParseBlock();
        }
        return new ConstructorDecl(accessModifier, ctorParameters, ctorBody);
      }

      OverrideModifier overrideModifier = ParseOptionalOverrideModifier();
      bool isNative = _stream.Match(ALKScriptTokenType.Native);
      ALKScriptToken? asyncKeyword = _stream.Check(ALKScriptTokenType.Async) ? _stream.Peek() : null;
      bool isAsync = _stream.Match(ALKScriptTokenType.Async);

      if (overrideModifier != OverrideModifier.None || isNative || isAsync || _stream.Check(ALKScriptTokenType.Function))
      {
        _stream.Consume(ALKScriptTokenType.Function, "Expect 'function' in a method declaration.");

        List<string> typeParameters = ParseOptionalTypeParameters();
        TypeNode returnType = ParseType();
        ALKScriptToken methodName = _stream.Consume(ALKScriptTokenType.Identifier, "Expect a method name.");
        List<Parameter> parameters = ParseParameterList();

        BlockStmt? body;
        bool asyncBodyHasAwait;

        using (_parsingContext.EnterFunctionBody(isAsync))
        {
          if (isNative)
          {
            body = ParseFunctionOrMethodBody(isNative: true, declarationKind: "method");
          }
          else if (_stream.Match(ALKScriptTokenType.Semicolon))
          {
            body = null;
          }
          else
          {
            body = ParseBlock();
          }
          asyncBodyHasAwait = _parsingContext.BodyHasAwait;
        }

        // 'async' on a non-native, non-abstract method requires at least one
        // 'await' in its body — the same rule as free functions. Abstract methods
        // are exempt because they defer the body (and thus the awaits) to the
        // override; native methods are exempt because the host provides the body.
        bool isAbstract = overrideModifier == OverrideModifier.Abstract;
        if (isAsync && !isNative && !isAbstract && body != null && !asyncBodyHasAwait)
        {
          throw Error(asyncKeyword!, "'async' is only valid on methods whose body contains at least one 'await' expression. Remove 'async' or add an 'await' call.");
        }

        return new MethodDecl(accessModifier, overrideModifier, isNative, isAsync, typeParameters, returnType, methodName, parameters, body);
      }

      // Field: accessModifier? ("var" | type) IDENTIFIER ("=" expression)? ";"
      TypeNode? fieldType = _stream.Match(ALKScriptTokenType.Var) ? null : ParseType();
      ALKScriptToken fieldName = _stream.Consume(ALKScriptTokenType.Identifier, "Expect a field name.");

      Expr? fieldInitializer = null;

      if (_stream.Match(ALKScriptTokenType.Equal))
      {
        fieldInitializer = ParseExpression();
      }

      _stream.Consume(ALKScriptTokenType.Semicolon, "Expect ';' after field declaration.");

      return new FieldDecl(accessModifier, fieldType, fieldName, fieldInitializer);
    }

    private AccessModifier ParseOptionalAccessModifier()
    {
      if (_stream.Match(ALKScriptTokenType.Public))
      {
        return AccessModifier.Public;
      }

      if (_stream.Match(ALKScriptTokenType.Protected))
      {
        return AccessModifier.Protected;
      }

      if (_stream.Match(ALKScriptTokenType.Private))
      {
        return AccessModifier.Private;
      }

      // Defaults to "private" when omitted.
      return AccessModifier.Private;
    }

    private OverrideModifier ParseOptionalOverrideModifier()
    {
      if (_stream.Match(ALKScriptTokenType.Virtual))
      {
        return OverrideModifier.Virtual;
      }

      if (_stream.Match(ALKScriptTokenType.Abstract))
      {
        return OverrideModifier.Abstract;
      }

      if (_stream.Match(ALKScriptTokenType.Override))
      {
        return OverrideModifier.Override;
      }

      return OverrideModifier.None;
    }

    private bool CheckFunctionDeclStart()
    {
      int offset = 0;

      if (_stream.CheckAhead(offset, ALKScriptTokenType.Native))
      {
        offset++;
      }

      if (_stream.CheckAhead(offset, ALKScriptTokenType.Async))
      {
        offset++;
      }

      return _stream.CheckAhead(offset, ALKScriptTokenType.Function);
    }

    private FunctionDecl ParseFunctionDecl()
    {
      bool isNative = _stream.Match(ALKScriptTokenType.Native);
      ALKScriptToken? asyncKeyword = _stream.Check(ALKScriptTokenType.Async) ? _stream.Peek() : null;
      bool isAsync = _stream.Match(ALKScriptTokenType.Async);
      _stream.Consume(ALKScriptTokenType.Function, "Expect 'function'.");

      List<string> typeParameters = ParseOptionalTypeParameters();
      TypeNode returnType = ParseType();
      ALKScriptToken name = _stream.Consume(ALKScriptTokenType.Identifier, "Expect a function name.");
      List<Parameter> parameters = ParseParameterList();

      BlockStmt? body;
      bool asyncBodyHasAwait;
      using (_parsingContext.EnterFunctionBody(isAsync))
      {
        body = ParseFunctionOrMethodBody(isNative, "function");
        asyncBodyHasAwait = _parsingContext.BodyHasAwait;
      }

      // 'async' on a non-native function is only meaningful when the body
      // actually suspends on something — i.e. contains at least one 'await'.
      // Native functions are exempt (the host provides the async implementation);
      // a plain user-defined function that never awaits should not declare itself
      // async, since it would complete synchronously anyway.
      if (isAsync && !isNative && body != null && !asyncBodyHasAwait)
      {
        throw Error(asyncKeyword!, "'async' is only valid on functions whose body contains at least one 'await' expression. Remove 'async' or add an 'await' call.");
      }

      return new FunctionDecl(isNative, isAsync, typeParameters, returnType, name, parameters, body);
    }

    /// <summary>
    /// Parses the "( block | ';' )" tail shared by function and method
    /// declarations. "native" declarations must end with ";" — their
    /// implementation is supplied by the host runtime, not ALKScript source —
    /// so a null body is returned without attempting to parse a block.
    /// </summary>
    private BlockStmt? ParseFunctionOrMethodBody(bool isNative, string declarationKind)
    {
      if (isNative)
      {
        _stream.Consume(ALKScriptTokenType.Semicolon, $"Expect ';' after native {declarationKind} declaration.");
        return null;
      }

      return ParseBlock();
    }

    private List<string> ParseOptionalTypeParameters()
    {
      var typeParameters = new List<string>();

      if (!_stream.Match(ALKScriptTokenType.Less))
      {
        return typeParameters;
      }

      do
      {
        ALKScriptToken parameterName = _stream.Consume(ALKScriptTokenType.Identifier, "Expect a type parameter name.");
        typeParameters.Add(parameterName.Lexeme);
      } while (_stream.Match(ALKScriptTokenType.Comma));

      _stream.Consume(ALKScriptTokenType.Greater, "Expect '>' after type parameters.");

      return typeParameters;
    }

    private List<Parameter> ParseParameterList()
    {
      _stream.Consume(ALKScriptTokenType.LeftParen, "Expect '(' to begin a parameter list.");

      var parameters = new List<Parameter>();

      if (!_stream.Check(ALKScriptTokenType.RightParen))
      {
        do
        {
          TypeNode type = ParseType();
          ALKScriptToken name = _stream.Consume(ALKScriptTokenType.Identifier, "Expect a parameter name.");
          parameters.Add(new Parameter(type, name.Lexeme));
        } while (_stream.Match(ALKScriptTokenType.Comma));
      }

      _stream.Consume(ALKScriptTokenType.RightParen, "Expect ')' after parameter list.");

      return parameters;
    }

    /// <summary>
    /// Attempts to parse a variable declaration ("var" | type) IDENTIFIER
    /// ("=" expression)? ";" starting at the current position. If the token
    /// sequence does not match (e.g. it is actually an expression statement
    /// such as a call or assignment), the parser position is restored and
    /// false is returned.
    /// </summary>
    private bool TryParseVariableDecl(out VariableDecl? declaration)
    {
      int checkpoint = _stream.Position;

      bool isVar = _stream.Match(ALKScriptTokenType.Var);
      TypeNode? type = null;

      if (!isVar)
      {
        if (!CanStartType())
        {
          declaration = null;
          return false;
        }

        try
        {
          type = ParseType();
        }
        catch (ParseException)
        {
          _stream.Position = checkpoint;
          declaration = null;
          return false;
        }
      }

      if (!_stream.Check(ALKScriptTokenType.Identifier))
      {
        _stream.Position = checkpoint;
        declaration = null;
        return false;
      }

      ALKScriptToken name = _stream.Advance();
      Expr? initializer = null;

      if (_stream.Match(ALKScriptTokenType.Equal))
      {
        initializer = ParseExpression();
      }

      if (!_stream.Check(ALKScriptTokenType.Semicolon))
      {
        _stream.Position = checkpoint;
        declaration = null;
        return false;
      }

      _stream.Advance();

      declaration = new VariableDecl(type, name, initializer);
      return true;
    }

    /// <summary>True when the current token can begin a type annotation.</summary>
    private bool CanStartType()
    {
      return PrimitiveTypeNames.ContainsKey(_stream.Peek().Type) || _stream.Check(ALKScriptTokenType.Identifier);
    }

    #endregion

    #region Types

    private TypeNode ParseType()
    {
      ALKScriptToken token = _stream.Advance();
      string name;

      if (PrimitiveTypeNames.TryGetValue(token.Type, out string? primitiveName))
      {
        name = primitiveName!;
      }
      else if (token.Type == ALKScriptTokenType.Identifier)
      {
        name = token.Lexeme;
      }
      else
      {
        throw Error(token, "Expect a type name.");
      }

      var typeArguments = new List<TypeNode>();

      if (_stream.Match(ALKScriptTokenType.Less))
      {
        do
        {
          typeArguments.Add(ParseType());
        } while (_stream.Match(ALKScriptTokenType.Comma));

        _stream.Consume(ALKScriptTokenType.Greater, "Expect '>' after type arguments.");
      }

      int arrayRank = 0;

      while (_stream.Check(ALKScriptTokenType.LeftBracket) && _stream.CheckNext(ALKScriptTokenType.RightBracket))
      {
        _stream.Advance();
        _stream.Advance();
        arrayRank++;
      }

      bool isNullable = _stream.Match(ALKScriptTokenType.Question);

      return new TypeNode(name, typeArguments, arrayRank, isNullable);
    }

    #endregion

    #region Statements

    private Stmt ParseStatement()
    {
      if (_stream.Match(ALKScriptTokenType.If))
      {
        return ParseIfStatement();
      }

      if (_stream.Match(ALKScriptTokenType.While))
      {
        return ParseWhileStatement();
      }

      if (_stream.Match(ALKScriptTokenType.For))
      {
        return ParseForStatement();
      }

      if (_stream.Match(ALKScriptTokenType.Return))
      {
        return ParseReturnStatement();
      }

      if (_stream.Match(ALKScriptTokenType.Break))
      {
        return ParseBreakStatement();
      }

      if (_stream.Match(ALKScriptTokenType.Continue))
      {
        return ParseContinueStatement();
      }

      if (_stream.Match(ALKScriptTokenType.Foreach))
      {
        return ParseForeachStatement();
      }

      if (_stream.Match(ALKScriptTokenType.Do))
      {
        return ParseDoWhileStatement();
      }

      if (_stream.Match(ALKScriptTokenType.Switch))
      {
        return ParseSwitchStatement();
      }

      if (_stream.Match(ALKScriptTokenType.Try))
      {
        return ParseTryStatement();
      }

      if (_stream.Match(ALKScriptTokenType.Throw))
      {
        return ParseThrowStatement();
      }

      if (_stream.Check(ALKScriptTokenType.LeftBrace))
      {
        return ParseBlock();
      }

      return ParseExpressionStatement();
    }

    private Stmt ParseIfStatement()
    {
      _stream.Consume(ALKScriptTokenType.LeftParen, "Expect '(' after 'if'.");
      Expr condition = ParseExpression();
      _stream.Consume(ALKScriptTokenType.RightParen, "Expect ')' after if condition.");

      Stmt thenBranch = ParseStatement();
      Stmt? elseBranch = null;

      if (_stream.Match(ALKScriptTokenType.Else))
      {
        elseBranch = ParseStatement();
      }

      return new IfStmt(condition, thenBranch, elseBranch);
    }

    private Stmt ParseWhileStatement()
    {
      _stream.Consume(ALKScriptTokenType.LeftParen, "Expect '(' after 'while'.");
      Expr condition = ParseExpression();
      _stream.Consume(ALKScriptTokenType.RightParen, "Expect ')' after while condition.");

      Stmt body = ParseStatement();

      return new WhileStmt(condition, body);
    }

    private Stmt ParseForeachStatement()
    {
      ALKScriptToken keyword = _stream.Previous();
      _stream.Consume(ALKScriptTokenType.LeftParen, "Expect '(' after 'foreach'.");
      _stream.Consume(ALKScriptTokenType.Var, "Expect 'var' in foreach variable declaration.");
      ALKScriptToken variable = _stream.Consume(ALKScriptTokenType.Identifier, "Expect a variable name in foreach.");
      _stream.Consume(ALKScriptTokenType.In, "Expect 'in' after variable name in foreach.");
      Expr collection = ParseExpression();
      _stream.Consume(ALKScriptTokenType.RightParen, "Expect ')' after foreach collection.");
      Stmt body = ParseStatement();
      return new ForeachStmt(keyword, variable, collection, body);
    }

    private Stmt ParseDoWhileStatement()
    {
      ALKScriptToken keyword = _stream.Previous();
      Stmt body = ParseStatement();
      _stream.Consume(ALKScriptTokenType.While, "Expect 'while' after 'do' body.");
      _stream.Consume(ALKScriptTokenType.LeftParen, "Expect '(' after 'while'.");
      Expr condition = ParseExpression();
      _stream.Consume(ALKScriptTokenType.RightParen, "Expect ')' after do-while condition.");
      _stream.Consume(ALKScriptTokenType.Semicolon, "Expect ';' after do-while statement.");
      return new DoWhileStmt(keyword, body, condition);
    }

    private Stmt ParseSwitchStatement()
    {
      ALKScriptToken keyword = _stream.Previous();
      _stream.Consume(ALKScriptTokenType.LeftParen, "Expect '(' after 'switch'.");
      Expr discriminant = ParseExpression();
      _stream.Consume(ALKScriptTokenType.RightParen, "Expect ')' after switch discriminant.");
      _stream.Consume(ALKScriptTokenType.LeftBrace, "Expect '{' to begin switch body.");

      var cases = new List<SwitchCase>();
      bool seenDefault = false;

      while (!_stream.Check(ALKScriptTokenType.RightBrace) && !_stream.IsAtEnd())
      {
        Expr? test = null;

        if (_stream.Match(ALKScriptTokenType.Case))
        {
          test = ParseExpression();
          _stream.Consume(ALKScriptTokenType.Colon, "Expect ':' after 'case' value.");
        }
        else if (_stream.Match(ALKScriptTokenType.Default))
        {
          if (seenDefault)
          {
            throw Error(_stream.Previous(), "A switch statement may only have one 'default' case.");
          }
          seenDefault = true;
          _stream.Consume(ALKScriptTokenType.Colon, "Expect ':' after 'default'.");
        }
        else
        {
          throw Error(_stream.Peek(), "Expect 'case' or 'default' inside switch body.");
        }

        var body = new List<Stmt>();
        while (!_stream.Check(ALKScriptTokenType.Case) &&
               !_stream.Check(ALKScriptTokenType.Default) &&
               !_stream.Check(ALKScriptTokenType.RightBrace) &&
               !_stream.IsAtEnd())
        {
          body.Add(ParseDeclaration());
        }

        cases.Add(new SwitchCase(test, body));
      }

      _stream.Consume(ALKScriptTokenType.RightBrace, "Expect '}' after switch body.");
      return new SwitchStmt(keyword, discriminant, cases);
    }

    private Stmt ParseForStatement()
    {
      _stream.Consume(ALKScriptTokenType.LeftParen, "Expect '(' after 'for'.");

      Stmt? initializer;

      if (_stream.Match(ALKScriptTokenType.Semicolon))
      {
        initializer = null;
      }
      else if (TryParseVariableDecl(out VariableDecl? variableDecl))
      {
        initializer = variableDecl;
      }
      else
      {
        initializer = ParseExpressionStatement();
      }

      Expr? condition = null;

      if (!_stream.Check(ALKScriptTokenType.Semicolon))
      {
        condition = ParseExpression();
      }

      _stream.Consume(ALKScriptTokenType.Semicolon, "Expect ';' after loop condition.");

      Expr? increment = null;

      if (!_stream.Check(ALKScriptTokenType.RightParen))
      {
        increment = ParseExpression();
      }

      _stream.Consume(ALKScriptTokenType.RightParen, "Expect ')' after for clauses.");

      Stmt body = ParseStatement();

      return new ForStmt(initializer, condition, increment, body);
    }

    private Stmt ParseReturnStatement()
    {
      ALKScriptToken keyword = _stream.Previous();
      Expr? value = null;

      if (!_stream.Check(ALKScriptTokenType.Semicolon))
      {
        value = ParseExpression();
      }

      _stream.Consume(ALKScriptTokenType.Semicolon, "Expect ';' after return statement.");

      return new ReturnStmt(keyword, value);
    }

    private Stmt ParseBreakStatement()
    {
      ALKScriptToken keyword = _stream.Previous();
      _stream.Consume(ALKScriptTokenType.Semicolon, "Expect ';' after 'break'.");
      return new BreakStmt(keyword);
    }

    private Stmt ParseContinueStatement()
    {
      ALKScriptToken keyword = _stream.Previous();
      _stream.Consume(ALKScriptTokenType.Semicolon, "Expect ';' after 'continue'.");
      return new ContinueStmt(keyword);
    }

    private Stmt ParseThrowStatement()
    {
      ALKScriptToken keyword = _stream.Previous();
      Expr value = ParseExpression();
      _stream.Consume(ALKScriptTokenType.Semicolon, "Expect ';' after throw statement.");

      return new ThrowStmt(keyword, value);
    }

    private Stmt ParseTryStatement()
    {
      BlockStmt tryBlock = ParseBlock();
      var catchClauses = new List<CatchClause>();

      while (_stream.Match(ALKScriptTokenType.Catch))
      {
        TypeNode? exceptionType = null;
        ALKScriptToken? exceptionName = null;

        if (_stream.Match(ALKScriptTokenType.LeftParen))
        {
          exceptionType = ParseType();
          exceptionName = _stream.Consume(ALKScriptTokenType.Identifier, "Expect a binding name in catch clause.");
          _stream.Consume(ALKScriptTokenType.RightParen, "Expect ')' after catch clause binding.");
        }

        BlockStmt catchBody = ParseBlock();
        catchClauses.Add(new CatchClause(exceptionType, exceptionName, catchBody));
      }

      BlockStmt? finallyBlock = null;

      if (_stream.Match(ALKScriptTokenType.Finally))
      {
        finallyBlock = ParseBlock();
      }

      if (catchClauses.Count == 0 && finallyBlock == null)
      {
        throw Error(_stream.Peek(), "Expect 'catch' or 'finally' after 'try' block.");
      }

      return new TryStmt(tryBlock, catchClauses, finallyBlock);
    }

    private BlockStmt ParseBlock()
    {
      _stream.Consume(ALKScriptTokenType.LeftBrace, "Expect '{' to begin a block.");

      var statements = new List<Stmt>();

      while (!_stream.Check(ALKScriptTokenType.RightBrace) && !_stream.IsAtEnd())
      {
        statements.Add(ParseDeclaration());
      }

      _stream.Consume(ALKScriptTokenType.RightBrace, "Expect '}' after block.");

      return new BlockStmt(statements);
    }

    private Stmt ParseExpressionStatement()
    {
      Expr expression = ParseExpression();
      _stream.Consume(ALKScriptTokenType.Semicolon, "Expect ';' after expression.");

      return new ExpressionStmt(expression);
    }

    #endregion

    #region Expressions

    private Expr ParseExpression()
    {
      return ParseAssignment();
    }

    private Expr ParseAssignment()
    {
      Expr target = ParseTernary();

      if (_stream.Match(ALKScriptTokenType.Equal))
      {
        ALKScriptToken equals = _stream.Previous();
        Expr value = ParseAssignment();

        if (target is IdentifierExpr || target is GetExpr || target is IndexExpr)
        {
          return new AssignmentExpr(target, value);
        }

        throw Error(equals, "Invalid assignment target.");
      }

      if (_stream.Match(
            ALKScriptTokenType.PlusEqual, ALKScriptTokenType.MinusEqual,
            ALKScriptTokenType.StarEqual, ALKScriptTokenType.SlashEqual,
            ALKScriptTokenType.PercentEqual))
      {
        ALKScriptToken op = _stream.Previous();
        Expr value = ParseAssignment();

        if (target is IdentifierExpr || target is GetExpr || target is IndexExpr)
        {
          return new CompoundAssignmentExpr(target, op, value);
        }

        throw Error(op, "Invalid assignment target for compound assignment.");
      }

      return target;
    }

    private Expr ParseTernary()
    {
      Expr condition = ParseNullCoalescing();

      if (_stream.Match(ALKScriptTokenType.Question))
      {
        ALKScriptToken question = _stream.Previous();
        Expr thenExpr = ParseAssignment();
        _stream.Consume(ALKScriptTokenType.Colon, "Expect ':' after ternary 'then' branch.");
        Expr elseExpr = ParseAssignment();
        return new TernaryExpr(condition, question, thenExpr, elseExpr);
      }

      return condition;
    }

    private Expr ParseNullCoalescing()
    {
      Expr expr = ParseLogicOr();

      while (_stream.Match(ALKScriptTokenType.QuestionQuestion))
      {
        ALKScriptToken op = _stream.Previous();
        Expr right = ParseLogicOr();
        expr = new BinaryExpr(expr, op, right);
      }

      return expr;
    }

    private Expr ParseLogicOr()
    {
      Expr expr = ParseLogicAnd();

      while (_stream.Match(ALKScriptTokenType.PipePipe))
      {
        ALKScriptToken op = _stream.Previous();
        Expr right = ParseLogicAnd();
        expr = new BinaryExpr(expr, op, right);
      }

      return expr;
    }

    private Expr ParseLogicAnd()
    {
      Expr expr = ParseEquality();

      while (_stream.Match(ALKScriptTokenType.AmpAmp))
      {
        ALKScriptToken op = _stream.Previous();
        Expr right = ParseEquality();
        expr = new BinaryExpr(expr, op, right);
      }

      return expr;
    }

    private Expr ParseEquality()
    {
      Expr expr = ParseComparison();

      while (_stream.Match(ALKScriptTokenType.EqualEqual, ALKScriptTokenType.BangEqual))
      {
        ALKScriptToken op = _stream.Previous();
        Expr right = ParseComparison();
        expr = new BinaryExpr(expr, op, right);
      }

      return expr;
    }

    private Expr ParseComparison()
    {
      Expr expr = ParseTerm();

      while (_stream.Match(ALKScriptTokenType.Less, ALKScriptTokenType.LessEqual, ALKScriptTokenType.Greater, ALKScriptTokenType.GreaterEqual))
      {
        ALKScriptToken op = _stream.Previous();
        Expr right = ParseTerm();
        expr = new BinaryExpr(expr, op, right);
      }

      return expr;
    }

    private Expr ParseTerm()
    {
      Expr expr = ParseFactor();

      while (_stream.Match(ALKScriptTokenType.Plus, ALKScriptTokenType.Minus))
      {
        ALKScriptToken op = _stream.Previous();
        Expr right = ParseFactor();
        expr = new BinaryExpr(expr, op, right);
      }

      return expr;
    }

    private Expr ParseFactor()
    {
      Expr expr = ParseUnary();

      while (_stream.Match(ALKScriptTokenType.Star, ALKScriptTokenType.Slash, ALKScriptTokenType.Percent))
      {
        ALKScriptToken op = _stream.Previous();
        Expr right = ParseUnary();
        expr = new BinaryExpr(expr, op, right);
      }

      return expr;
    }

    private Expr ParseUnary()
    {
      if (_stream.Match(ALKScriptTokenType.PlusPlus, ALKScriptTokenType.MinusMinus))
      {
        ALKScriptToken op = _stream.Previous();
        Expr operand = ParseUnary();
        if (!(operand is IdentifierExpr || operand is GetExpr || operand is IndexExpr))
          throw Error(op, $"'{op.Lexeme}' requires an assignable target (variable, field, or array element).");
        return new PrefixUpdateExpr(op, operand);
      }

      if (_stream.Match(ALKScriptTokenType.Bang, ALKScriptTokenType.Minus))
      {
        ALKScriptToken op = _stream.Previous();
        Expr operand = ParseUnary();
        return new UnaryExpr(op, operand);
      }

      if (_stream.Match(ALKScriptTokenType.Await))
      {
        ALKScriptToken keyword = _stream.Previous();

        if (!_parsingContext.InAsyncBody)
        {
          throw Error(keyword, "'await' is only valid inside an 'async' function or method.");
        }

        _parsingContext.NoteAwait();
        Expr operand = ParseUnary();
        return new AwaitExpr(keyword, operand);
      }

      return ParseCall();
    }

    private Expr ParseCall()
    {
      Expr expr = ParsePrimary();

      while (true)
      {
        if (_stream.Match(ALKScriptTokenType.LeftParen))
        {
          expr = FinishCall(expr);
        }
        else if (_stream.Match(ALKScriptTokenType.Dot))
        {
          ALKScriptToken name = _stream.Consume(ALKScriptTokenType.Identifier, "Expect a property or method name after '.'.");
          expr = new GetExpr(expr, name);
        }
        else if (_stream.Match(ALKScriptTokenType.QuestionDot))
        {
          ALKScriptToken name = _stream.Consume(ALKScriptTokenType.Identifier, "Expect a property or method name after '?.'.");
          expr = new NullConditionalGetExpr(expr, name);
        }
        else if (_stream.Match(ALKScriptTokenType.LeftBracket))
        {
          Expr index = ParseExpression();
          ALKScriptToken closingBracket = _stream.Consume(ALKScriptTokenType.RightBracket, "Expect ']' after index expression.");
          expr = new IndexExpr(expr, index, closingBracket);
        }
        else
        {
          break;
        }
      }

      if (_stream.Match(ALKScriptTokenType.PlusPlus, ALKScriptTokenType.MinusMinus))
      {
        ALKScriptToken op = _stream.Previous();
        if (!(expr is IdentifierExpr || expr is GetExpr || expr is IndexExpr))
          throw Error(op, $"'{op.Lexeme}' requires an assignable target (variable, field, or array element).");
        return new PostfixUpdateExpr(expr, op);
      }

      return expr;
    }

    private Expr FinishCall(Expr callee)
    {
      var arguments = new List<Expr>();

      if (!_stream.Check(ALKScriptTokenType.RightParen))
      {
        do
        {
          arguments.Add(ParseExpression());
        } while (_stream.Match(ALKScriptTokenType.Comma));
      }

      ALKScriptToken closingParen = _stream.Consume(ALKScriptTokenType.RightParen, "Expect ')' after arguments.");

      return new CallExpr(callee, closingParen, arguments);
    }

    private Expr ParsePrimary()
    {
      if (_stream.Match(ALKScriptTokenType.False))
      {
        return new LiteralExpr(_stream.Previous(), false);
      }

      if (_stream.Match(ALKScriptTokenType.True))
      {
        return new LiteralExpr(_stream.Previous(), true);
      }

      if (_stream.Match(ALKScriptTokenType.Null))
      {
        return new LiteralExpr(_stream.Previous(), null);
      }

      if (_stream.Match(ALKScriptTokenType.Number))
      {
        ALKScriptToken token = _stream.Previous();
        return new LiteralExpr(token, ParseNumberLiteral(token));
      }

      if (_stream.Match(ALKScriptTokenType.String))
      {
        ALKScriptToken token = _stream.Previous();
        return new LiteralExpr(token, token.Lexeme);
      }

      if (_stream.Match(ALKScriptTokenType.This))
      {
        return new ThisExpr(_stream.Previous());
      }

      if (_stream.Match(ALKScriptTokenType.Base))
      {
        return new BaseExpr(_stream.Previous());
      }

      if (_stream.Match(ALKScriptTokenType.New))
      {
        return ParseNewExpression();
      }

      if (_stream.Match(ALKScriptTokenType.Identifier))
      {
        return new IdentifierExpr(_stream.Previous());
      }

      if (_stream.Match(ALKScriptTokenType.LeftParen))
      {
        Expr expression = ParseExpression();
        _stream.Consume(ALKScriptTokenType.RightParen, "Expect ')' after expression.");
        return new GroupingExpr(expression);
      }

      if (_stream.Match(ALKScriptTokenType.LeftBracket))
      {
        var elements = new List<Expr>();

        if (!_stream.Check(ALKScriptTokenType.RightBracket))
        {
          do
          {
            elements.Add(ParseExpression());
          } while (_stream.Match(ALKScriptTokenType.Comma));
        }

        _stream.Consume(ALKScriptTokenType.RightBracket, "Expect ']' after array literal elements.");

        return new ArrayLiteralExpr(elements);
      }

      throw Error(_stream.Peek(), "Expect an expression.");
    }

    private Expr ParseNewExpression()
    {
      ALKScriptToken keyword = _stream.Previous();
      ALKScriptToken typeName = _stream.Consume(ALKScriptTokenType.Identifier, "Expect a type name after 'new'.");

      var typeArguments = new List<TypeNode>();

      if (_stream.Match(ALKScriptTokenType.Less))
      {
        do
        {
          typeArguments.Add(ParseType());
        } while (_stream.Match(ALKScriptTokenType.Comma));

        _stream.Consume(ALKScriptTokenType.Greater, "Expect '>' after type arguments.");
      }

      _stream.Consume(ALKScriptTokenType.LeftParen, "Expect '(' after type name in 'new' expression.");

      var arguments = new List<Expr>();

      if (!_stream.Check(ALKScriptTokenType.RightParen))
      {
        do
        {
          arguments.Add(ParseExpression());
        } while (_stream.Match(ALKScriptTokenType.Comma));
      }

      _stream.Consume(ALKScriptTokenType.RightParen, "Expect ')' after constructor arguments.");

      return new NewExpr(keyword, typeName, typeArguments, arguments);
    }

    /// <summary>
    /// Converts a "Number" token's lexeme into an int, long, or double, per
    /// the literal rules in §1.4: a trailing "L"/"l" marks a long, a decimal
    /// point marks a float (represented here as a double), and otherwise the
    /// value is an int.
    /// </summary>
    private static object ParseNumberLiteral(ALKScriptToken token)
    {
      string lexeme = token.Lexeme;

      if (lexeme.Length > 0 && (lexeme[lexeme.Length - 1] == 'L' || lexeme[lexeme.Length - 1] == 'l'))
      {
        string digits = lexeme.Substring(0, lexeme.Length - 1);
        return long.Parse(digits, CultureInfo.InvariantCulture);
      }

      if (lexeme.Contains("."))
      {
        return double.Parse(lexeme, CultureInfo.InvariantCulture);
      }

      if (int.TryParse(lexeme, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intValue))
      {
        return intValue;
      }

      return long.Parse(lexeme, CultureInfo.InvariantCulture);
    }

    #endregion

    #region Errors

    private static ParseException Error(ALKScriptToken token, string message)
    {
      return new ParseException(token, message);
    }

    #endregion

    /// <summary>
    /// Tracks parsing context that's relevant several frames down the
    /// recursive descent — currently just "are we inside an <c>async</c>
    /// function/method body, so that <c>await</c> is valid here?" (see
    /// docs/ASYNC_AWAIT_DESIGN.md decisions #4 and #9), generalizing to e.g.
    /// a future <c>break</c>/<c>continue</c>-must-be-in-a-loop check by
    /// adding one field and one <c>EnterX</c>/<c>InX</c> pair.
    ///
    /// Each <c>EnterX</c> returns an <see cref="IDisposable"/> scope guard
    /// that saves the previous state and restores it on disposal — rather
    /// than just setting a flag — so that e.g. a non-<c>async</c> function
    /// nested inside an <c>async</c> one is correctly tracked as its own,
    /// non-async, context.
    /// </summary>
    private sealed class ParsingContext
    {
      private bool _inAsyncBody;
      private bool _bodyHasAwait;

      /// <summary>
      /// Enters a function/method (or constructor, or the entry module's top
      /// level) body, tracking whether <c>await</c> is valid directly within
      /// it, and returns a guard that restores the enclosing context's state
      /// when the body has been fully parsed.
      /// </summary>
      public IDisposable EnterFunctionBody(bool isAsync)
      {
        bool previousAsync = _inAsyncBody;
        bool previousHasAwait = _bodyHasAwait;
        _inAsyncBody = isAsync;
        _bodyHasAwait = false;
        return new Restorer(() =>
        {
          _inAsyncBody = previousAsync;
          _bodyHasAwait = previousHasAwait;
        });
      }

      public bool InAsyncBody => _inAsyncBody;

      /// <summary>
      /// Whether the current body (since the most recent
      /// <see cref="EnterFunctionBody"/>) contains at least one <c>await</c>
      /// expression at its own scope — not inside a nested function body.
      /// Captured just before exiting a body scope to validate the
      /// "async requires await" rule.
      /// </summary>
      public bool BodyHasAwait => _bodyHasAwait;

      /// <summary>
      /// Records that the current body has encountered an <c>await</c>
      /// expression. Called from <see cref="ALKScriptParser.ParseUnary"/> when
      /// an <c>await</c> token is consumed.
      /// </summary>
      public void NoteAwait() => _bodyHasAwait = true;

      private sealed class Restorer : IDisposable
      {
        private readonly Action _restore;

        public Restorer(Action restore)
        {
          _restore = restore;
        }

        public void Dispose() => _restore();
      }
    }
  }
}
