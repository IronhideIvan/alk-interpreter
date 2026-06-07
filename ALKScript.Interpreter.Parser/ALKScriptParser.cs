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

      while (!_stream.IsAtEnd())
      {
        declarations.Add(ParseDeclaration());
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
      if (_stream.Check(ALKScriptTokenType.Class))
      {
        return true;
      }

      return _stream.Check(ALKScriptTokenType.Abstract) && _stream.CheckNext(ALKScriptTokenType.Class);
    }

    private ClassDecl ParseClassDecl()
    {
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

      return new ClassDecl(isAbstract, name, typeParameters, superclassName, superclassTypeArguments, members);
    }

    private MemberDecl ParseMember()
    {
      AccessModifier accessModifier = ParseOptionalAccessModifier();

      // Constructor: accessModifier? "new" "(" parameters? ")" block
      if (_stream.Check(ALKScriptTokenType.New))
      {
        _stream.Advance();
        List<Parameter> ctorParameters = ParseParameterList();
        BlockStmt ctorBody = ParseBlock();
        return new ConstructorDecl(accessModifier, ctorParameters, ctorBody);
      }

      OverrideModifier overrideModifier = ParseOptionalOverrideModifier();
      bool isNative = _stream.Match(ALKScriptTokenType.Native);
      bool isAsync = _stream.Match(ALKScriptTokenType.Async);

      if (overrideModifier != OverrideModifier.None || isNative || isAsync || _stream.Check(ALKScriptTokenType.Function))
      {
        _stream.Consume(ALKScriptTokenType.Function, "Expect 'function' in a method declaration.");

        List<string> typeParameters = ParseOptionalTypeParameters();
        TypeNode returnType = ParseType();
        ALKScriptToken methodName = _stream.Consume(ALKScriptTokenType.Identifier, "Expect a method name.");
        List<Parameter> parameters = ParseParameterList();

        BlockStmt? body;

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
      bool isAsync = _stream.Match(ALKScriptTokenType.Async);
      _stream.Consume(ALKScriptTokenType.Function, "Expect 'function'.");

      List<string> typeParameters = ParseOptionalTypeParameters();
      TypeNode returnType = ParseType();
      ALKScriptToken name = _stream.Consume(ALKScriptTokenType.Identifier, "Expect a function name.");
      List<Parameter> parameters = ParseParameterList();
      BlockStmt? body = ParseFunctionOrMethodBody(isNative, "function");

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
      Expr target = ParseLogicOr();

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

      return target;
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
      if (_stream.Match(ALKScriptTokenType.Bang, ALKScriptTokenType.Minus))
      {
        ALKScriptToken op = _stream.Previous();
        Expr operand = ParseUnary();
        return new UnaryExpr(op, operand);
      }

      if (_stream.Match(ALKScriptTokenType.Await))
      {
        ALKScriptToken keyword = _stream.Previous();
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
  }
}
