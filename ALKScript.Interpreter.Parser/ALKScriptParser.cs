using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ALKScript.Interpreter.Common;
using ALKScript.Interpreter.Parser.Ast;

namespace ALKScript.Interpreter.Parser
{
  /// <summary>
  /// A recursive-descent parser that turns the token stream produced by
  /// <see cref="IScriptLexer"/> into an abstract syntax tree, following the
  /// grammar described in §3 of the ALKScript language specification
  /// (docs/LANGUAGE_SPEC.md).
  /// </summary>
  public class ALKScriptParser
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

    private readonly List<ALKScriptToken> _tokens;
    private int _current;

    public ALKScriptParser(IEnumerable<ALKScriptToken> tokens)
    {
      _tokens = tokens.ToList();
    }

    /// <summary>Parses the entire token stream into a <see cref="ProgramNode"/>.</summary>
    public ProgramNode ParseProgram()
    {
      var imports = new List<ImportDecl>();

      while (Check(ALKScriptTokenType.Import))
      {
        imports.Add(ParseImportDecl());
      }

      var declarations = new List<Stmt>();

      while (!IsAtEnd())
      {
        declarations.Add(ParseDeclaration());
      }

      return new ProgramNode(imports, declarations);
    }

    #region Imports

    private ImportDecl ParseImportDecl()
    {
      Consume(ALKScriptTokenType.Import, "Expect 'import'.");

      ImportClause clause = ParseImportClause();

      Consume(ALKScriptTokenType.From, "Expect 'from' after import clause.");
      ALKScriptToken source = Consume(ALKScriptTokenType.String, "Expect a module path string after 'from'.");
      Consume(ALKScriptTokenType.Semicolon, "Expect ';' after import declaration.");

      return new ImportDecl(clause, source);
    }

    private ImportClause ParseImportClause()
    {
      if (Match(ALKScriptTokenType.Star))
      {
        Consume(ALKScriptTokenType.As, "Expect 'as' after '*' in a namespace import.");
        ALKScriptToken alias = Consume(ALKScriptTokenType.Identifier, "Expect a binding name after 'as'.");
        return new NamespaceImportClause(alias);
      }

      Consume(ALKScriptTokenType.LeftBrace, "Expect '{' to begin a named-imports clause.");

      var specifiers = new List<ImportSpecifier>();

      if (!Check(ALKScriptTokenType.RightBrace))
      {
        do
        {
          specifiers.Add(ParseImportSpecifier());
        } while (Match(ALKScriptTokenType.Comma));
      }

      Consume(ALKScriptTokenType.RightBrace, "Expect '}' after named imports.");

      return new NamedImportsClause(specifiers);
    }

    private ImportSpecifier ParseImportSpecifier()
    {
      ALKScriptToken name = Consume(ALKScriptTokenType.Identifier, "Expect an import name.");
      ALKScriptToken? alias = null;

      if (Match(ALKScriptTokenType.As))
      {
        alias = Consume(ALKScriptTokenType.Identifier, "Expect a binding name after 'as'.");
      }

      return new ImportSpecifier(name, alias);
    }

    #endregion

    #region Declarations

    private Decl ParseDeclaration()
    {
      if (Match(ALKScriptTokenType.Export))
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

      throw Error(Peek(), "Expect a class, function, or variable declaration after 'export'.");
    }

    private bool CheckClassDeclStart()
    {
      if (Check(ALKScriptTokenType.Class))
      {
        return true;
      }

      return Check(ALKScriptTokenType.Abstract) && CheckNext(ALKScriptTokenType.Class);
    }

    private ClassDecl ParseClassDecl()
    {
      bool isAbstract = Match(ALKScriptTokenType.Abstract);
      Consume(ALKScriptTokenType.Class, "Expect 'class'.");

      ALKScriptToken name = Consume(ALKScriptTokenType.Identifier, "Expect a class name.");
      List<string> typeParameters = ParseOptionalTypeParameters();

      ALKScriptToken? superclassName = null;
      List<TypeNode> superclassTypeArguments = new List<TypeNode>();

      if (Match(ALKScriptTokenType.Extends))
      {
        superclassName = Consume(ALKScriptTokenType.Identifier, "Expect a superclass name after 'extends'.");

        if (Match(ALKScriptTokenType.Less))
        {
          do
          {
            superclassTypeArguments.Add(ParseType());
          } while (Match(ALKScriptTokenType.Comma));

          Consume(ALKScriptTokenType.Greater, "Expect '>' after superclass type arguments.");
        }
      }

      Consume(ALKScriptTokenType.LeftBrace, "Expect '{' before class body.");

      var members = new List<MemberDecl>();

      while (!Check(ALKScriptTokenType.RightBrace) && !IsAtEnd())
      {
        members.Add(ParseMember());
      }

      Consume(ALKScriptTokenType.RightBrace, "Expect '}' after class body.");

      return new ClassDecl(isAbstract, name, typeParameters, superclassName, superclassTypeArguments, members);
    }

    private MemberDecl ParseMember()
    {
      AccessModifier accessModifier = ParseOptionalAccessModifier();

      // Constructor: accessModifier? "new" "(" parameters? ")" block
      if (Check(ALKScriptTokenType.New))
      {
        Advance();
        List<Parameter> ctorParameters = ParseParameterList();
        BlockStmt ctorBody = ParseBlock();
        return new ConstructorDecl(accessModifier, ctorParameters, ctorBody);
      }

      OverrideModifier overrideModifier = ParseOptionalOverrideModifier();
      bool isAsync = Match(ALKScriptTokenType.Async);

      if (overrideModifier != OverrideModifier.None || isAsync || Check(ALKScriptTokenType.Function))
      {
        Consume(ALKScriptTokenType.Function, "Expect 'function' in a method declaration.");

        List<string> typeParameters = ParseOptionalTypeParameters();
        TypeNode returnType = ParseType();
        ALKScriptToken methodName = Consume(ALKScriptTokenType.Identifier, "Expect a method name.");
        List<Parameter> parameters = ParseParameterList();

        BlockStmt? body;

        if (Match(ALKScriptTokenType.Semicolon))
        {
          body = null;
        }
        else
        {
          body = ParseBlock();
        }

        return new MethodDecl(accessModifier, overrideModifier, isAsync, typeParameters, returnType, methodName, parameters, body);
      }

      // Field: accessModifier? ("var" | type) IDENTIFIER ("=" expression)? ";"
      TypeNode? fieldType = Match(ALKScriptTokenType.Var) ? null : ParseType();
      ALKScriptToken fieldName = Consume(ALKScriptTokenType.Identifier, "Expect a field name.");

      Expr? fieldInitializer = null;

      if (Match(ALKScriptTokenType.Equal))
      {
        fieldInitializer = ParseExpression();
      }

      Consume(ALKScriptTokenType.Semicolon, "Expect ';' after field declaration.");

      return new FieldDecl(accessModifier, fieldType, fieldName, fieldInitializer);
    }

    private AccessModifier ParseOptionalAccessModifier()
    {
      if (Match(ALKScriptTokenType.Public))
      {
        return AccessModifier.Public;
      }

      if (Match(ALKScriptTokenType.Protected))
      {
        return AccessModifier.Protected;
      }

      if (Match(ALKScriptTokenType.Private))
      {
        return AccessModifier.Private;
      }

      // Defaults to "private" when omitted.
      return AccessModifier.Private;
    }

    private OverrideModifier ParseOptionalOverrideModifier()
    {
      if (Match(ALKScriptTokenType.Virtual))
      {
        return OverrideModifier.Virtual;
      }

      if (Match(ALKScriptTokenType.Abstract))
      {
        return OverrideModifier.Abstract;
      }

      if (Match(ALKScriptTokenType.Override))
      {
        return OverrideModifier.Override;
      }

      return OverrideModifier.None;
    }

    private bool CheckFunctionDeclStart()
    {
      if (Check(ALKScriptTokenType.Function))
      {
        return true;
      }

      return Check(ALKScriptTokenType.Async) && CheckNext(ALKScriptTokenType.Function);
    }

    private FunctionDecl ParseFunctionDecl()
    {
      bool isAsync = Match(ALKScriptTokenType.Async);
      Consume(ALKScriptTokenType.Function, "Expect 'function'.");

      List<string> typeParameters = ParseOptionalTypeParameters();
      TypeNode returnType = ParseType();
      ALKScriptToken name = Consume(ALKScriptTokenType.Identifier, "Expect a function name.");
      List<Parameter> parameters = ParseParameterList();
      BlockStmt body = ParseBlock();

      return new FunctionDecl(isAsync, typeParameters, returnType, name, parameters, body);
    }

    private List<string> ParseOptionalTypeParameters()
    {
      var typeParameters = new List<string>();

      if (!Match(ALKScriptTokenType.Less))
      {
        return typeParameters;
      }

      do
      {
        ALKScriptToken parameterName = Consume(ALKScriptTokenType.Identifier, "Expect a type parameter name.");
        typeParameters.Add(parameterName.Lexeme);
      } while (Match(ALKScriptTokenType.Comma));

      Consume(ALKScriptTokenType.Greater, "Expect '>' after type parameters.");

      return typeParameters;
    }

    private List<Parameter> ParseParameterList()
    {
      Consume(ALKScriptTokenType.LeftParen, "Expect '(' to begin a parameter list.");

      var parameters = new List<Parameter>();

      if (!Check(ALKScriptTokenType.RightParen))
      {
        do
        {
          TypeNode type = ParseType();
          ALKScriptToken name = Consume(ALKScriptTokenType.Identifier, "Expect a parameter name.");
          parameters.Add(new Parameter(type, name.Lexeme));
        } while (Match(ALKScriptTokenType.Comma));
      }

      Consume(ALKScriptTokenType.RightParen, "Expect ')' after parameter list.");

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
      int checkpoint = _current;

      bool isVar = Match(ALKScriptTokenType.Var);
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
          _current = checkpoint;
          declaration = null;
          return false;
        }
      }

      if (!Check(ALKScriptTokenType.Identifier))
      {
        _current = checkpoint;
        declaration = null;
        return false;
      }

      ALKScriptToken name = Advance();
      Expr? initializer = null;

      if (Match(ALKScriptTokenType.Equal))
      {
        initializer = ParseExpression();
      }

      if (!Check(ALKScriptTokenType.Semicolon))
      {
        _current = checkpoint;
        declaration = null;
        return false;
      }

      Advance();

      declaration = new VariableDecl(type, name, initializer);
      return true;
    }

    /// <summary>True when the current token can begin a type annotation.</summary>
    private bool CanStartType()
    {
      return PrimitiveTypeNames.ContainsKey(Peek().Type) || Check(ALKScriptTokenType.Identifier);
    }

    #endregion

    #region Types

    private TypeNode ParseType()
    {
      ALKScriptToken token = Advance();
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

      if (Match(ALKScriptTokenType.Less))
      {
        do
        {
          typeArguments.Add(ParseType());
        } while (Match(ALKScriptTokenType.Comma));

        Consume(ALKScriptTokenType.Greater, "Expect '>' after type arguments.");
      }

      int arrayRank = 0;

      while (Check(ALKScriptTokenType.LeftBracket) && CheckNext(ALKScriptTokenType.RightBracket))
      {
        Advance();
        Advance();
        arrayRank++;
      }

      bool isNullable = Match(ALKScriptTokenType.Question);

      return new TypeNode(name, typeArguments, arrayRank, isNullable);
    }

    #endregion

    #region Statements

    private Stmt ParseStatement()
    {
      if (Match(ALKScriptTokenType.If))
      {
        return ParseIfStatement();
      }

      if (Match(ALKScriptTokenType.While))
      {
        return ParseWhileStatement();
      }

      if (Match(ALKScriptTokenType.For))
      {
        return ParseForStatement();
      }

      if (Match(ALKScriptTokenType.Return))
      {
        return ParseReturnStatement();
      }

      if (Match(ALKScriptTokenType.Try))
      {
        return ParseTryStatement();
      }

      if (Match(ALKScriptTokenType.Throw))
      {
        return ParseThrowStatement();
      }

      if (Check(ALKScriptTokenType.LeftBrace))
      {
        return ParseBlock();
      }

      return ParseExpressionStatement();
    }

    private Stmt ParseIfStatement()
    {
      Consume(ALKScriptTokenType.LeftParen, "Expect '(' after 'if'.");
      Expr condition = ParseExpression();
      Consume(ALKScriptTokenType.RightParen, "Expect ')' after if condition.");

      Stmt thenBranch = ParseStatement();
      Stmt? elseBranch = null;

      if (Match(ALKScriptTokenType.Else))
      {
        elseBranch = ParseStatement();
      }

      return new IfStmt(condition, thenBranch, elseBranch);
    }

    private Stmt ParseWhileStatement()
    {
      Consume(ALKScriptTokenType.LeftParen, "Expect '(' after 'while'.");
      Expr condition = ParseExpression();
      Consume(ALKScriptTokenType.RightParen, "Expect ')' after while condition.");

      Stmt body = ParseStatement();

      return new WhileStmt(condition, body);
    }

    private Stmt ParseForStatement()
    {
      Consume(ALKScriptTokenType.LeftParen, "Expect '(' after 'for'.");

      Stmt? initializer;

      if (Match(ALKScriptTokenType.Semicolon))
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

      if (!Check(ALKScriptTokenType.Semicolon))
      {
        condition = ParseExpression();
      }

      Consume(ALKScriptTokenType.Semicolon, "Expect ';' after loop condition.");

      Expr? increment = null;

      if (!Check(ALKScriptTokenType.RightParen))
      {
        increment = ParseExpression();
      }

      Consume(ALKScriptTokenType.RightParen, "Expect ')' after for clauses.");

      Stmt body = ParseStatement();

      return new ForStmt(initializer, condition, increment, body);
    }

    private Stmt ParseReturnStatement()
    {
      ALKScriptToken keyword = Previous();
      Expr? value = null;

      if (!Check(ALKScriptTokenType.Semicolon))
      {
        value = ParseExpression();
      }

      Consume(ALKScriptTokenType.Semicolon, "Expect ';' after return statement.");

      return new ReturnStmt(keyword, value);
    }

    private Stmt ParseThrowStatement()
    {
      ALKScriptToken keyword = Previous();
      Expr value = ParseExpression();
      Consume(ALKScriptTokenType.Semicolon, "Expect ';' after throw statement.");

      return new ThrowStmt(keyword, value);
    }

    private Stmt ParseTryStatement()
    {
      BlockStmt tryBlock = ParseBlock();
      var catchClauses = new List<CatchClause>();

      while (Match(ALKScriptTokenType.Catch))
      {
        TypeNode? exceptionType = null;
        ALKScriptToken? exceptionName = null;

        if (Match(ALKScriptTokenType.LeftParen))
        {
          exceptionType = ParseType();
          exceptionName = Consume(ALKScriptTokenType.Identifier, "Expect a binding name in catch clause.");
          Consume(ALKScriptTokenType.RightParen, "Expect ')' after catch clause binding.");
        }

        BlockStmt catchBody = ParseBlock();
        catchClauses.Add(new CatchClause(exceptionType, exceptionName, catchBody));
      }

      BlockStmt? finallyBlock = null;

      if (Match(ALKScriptTokenType.Finally))
      {
        finallyBlock = ParseBlock();
      }

      if (catchClauses.Count == 0 && finallyBlock == null)
      {
        throw Error(Peek(), "Expect 'catch' or 'finally' after 'try' block.");
      }

      return new TryStmt(tryBlock, catchClauses, finallyBlock);
    }

    private BlockStmt ParseBlock()
    {
      Consume(ALKScriptTokenType.LeftBrace, "Expect '{' to begin a block.");

      var statements = new List<Stmt>();

      while (!Check(ALKScriptTokenType.RightBrace) && !IsAtEnd())
      {
        statements.Add(ParseDeclaration());
      }

      Consume(ALKScriptTokenType.RightBrace, "Expect '}' after block.");

      return new BlockStmt(statements);
    }

    private Stmt ParseExpressionStatement()
    {
      Expr expression = ParseExpression();
      Consume(ALKScriptTokenType.Semicolon, "Expect ';' after expression.");

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

      if (Match(ALKScriptTokenType.Equal))
      {
        ALKScriptToken equals = Previous();
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

      while (Match(ALKScriptTokenType.PipePipe))
      {
        ALKScriptToken op = Previous();
        Expr right = ParseLogicAnd();
        expr = new BinaryExpr(expr, op, right);
      }

      return expr;
    }

    private Expr ParseLogicAnd()
    {
      Expr expr = ParseEquality();

      while (Match(ALKScriptTokenType.AmpAmp))
      {
        ALKScriptToken op = Previous();
        Expr right = ParseEquality();
        expr = new BinaryExpr(expr, op, right);
      }

      return expr;
    }

    private Expr ParseEquality()
    {
      Expr expr = ParseComparison();

      while (Match(ALKScriptTokenType.EqualEqual, ALKScriptTokenType.BangEqual))
      {
        ALKScriptToken op = Previous();
        Expr right = ParseComparison();
        expr = new BinaryExpr(expr, op, right);
      }

      return expr;
    }

    private Expr ParseComparison()
    {
      Expr expr = ParseTerm();

      while (Match(ALKScriptTokenType.Less, ALKScriptTokenType.LessEqual, ALKScriptTokenType.Greater, ALKScriptTokenType.GreaterEqual))
      {
        ALKScriptToken op = Previous();
        Expr right = ParseTerm();
        expr = new BinaryExpr(expr, op, right);
      }

      return expr;
    }

    private Expr ParseTerm()
    {
      Expr expr = ParseFactor();

      while (Match(ALKScriptTokenType.Plus, ALKScriptTokenType.Minus))
      {
        ALKScriptToken op = Previous();
        Expr right = ParseFactor();
        expr = new BinaryExpr(expr, op, right);
      }

      return expr;
    }

    private Expr ParseFactor()
    {
      Expr expr = ParseUnary();

      while (Match(ALKScriptTokenType.Star, ALKScriptTokenType.Slash, ALKScriptTokenType.Percent))
      {
        ALKScriptToken op = Previous();
        Expr right = ParseUnary();
        expr = new BinaryExpr(expr, op, right);
      }

      return expr;
    }

    private Expr ParseUnary()
    {
      if (Match(ALKScriptTokenType.Bang, ALKScriptTokenType.Minus))
      {
        ALKScriptToken op = Previous();
        Expr operand = ParseUnary();
        return new UnaryExpr(op, operand);
      }

      if (Match(ALKScriptTokenType.Await))
      {
        ALKScriptToken keyword = Previous();
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
        if (Match(ALKScriptTokenType.LeftParen))
        {
          expr = FinishCall(expr);
        }
        else if (Match(ALKScriptTokenType.Dot))
        {
          ALKScriptToken name = Consume(ALKScriptTokenType.Identifier, "Expect a property or method name after '.'.");
          expr = new GetExpr(expr, name);
        }
        else if (Match(ALKScriptTokenType.LeftBracket))
        {
          Expr index = ParseExpression();
          ALKScriptToken closingBracket = Consume(ALKScriptTokenType.RightBracket, "Expect ']' after index expression.");
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

      if (!Check(ALKScriptTokenType.RightParen))
      {
        do
        {
          arguments.Add(ParseExpression());
        } while (Match(ALKScriptTokenType.Comma));
      }

      ALKScriptToken closingParen = Consume(ALKScriptTokenType.RightParen, "Expect ')' after arguments.");

      return new CallExpr(callee, closingParen, arguments);
    }

    private Expr ParsePrimary()
    {
      if (Match(ALKScriptTokenType.False))
      {
        return new LiteralExpr(Previous(), false);
      }

      if (Match(ALKScriptTokenType.True))
      {
        return new LiteralExpr(Previous(), true);
      }

      if (Match(ALKScriptTokenType.Null))
      {
        return new LiteralExpr(Previous(), null);
      }

      if (Match(ALKScriptTokenType.Number))
      {
        ALKScriptToken token = Previous();
        return new LiteralExpr(token, ParseNumberLiteral(token));
      }

      if (Match(ALKScriptTokenType.String))
      {
        ALKScriptToken token = Previous();
        return new LiteralExpr(token, token.Lexeme);
      }

      if (Match(ALKScriptTokenType.This))
      {
        return new ThisExpr(Previous());
      }

      if (Match(ALKScriptTokenType.Base))
      {
        return new BaseExpr(Previous());
      }

      if (Match(ALKScriptTokenType.New))
      {
        return ParseNewExpression();
      }

      if (Match(ALKScriptTokenType.Identifier))
      {
        return new IdentifierExpr(Previous());
      }

      if (Match(ALKScriptTokenType.LeftParen))
      {
        Expr expression = ParseExpression();
        Consume(ALKScriptTokenType.RightParen, "Expect ')' after expression.");
        return new GroupingExpr(expression);
      }

      if (Match(ALKScriptTokenType.LeftBracket))
      {
        var elements = new List<Expr>();

        if (!Check(ALKScriptTokenType.RightBracket))
        {
          do
          {
            elements.Add(ParseExpression());
          } while (Match(ALKScriptTokenType.Comma));
        }

        Consume(ALKScriptTokenType.RightBracket, "Expect ']' after array literal elements.");

        return new ArrayLiteralExpr(elements);
      }

      throw Error(Peek(), "Expect an expression.");
    }

    private Expr ParseNewExpression()
    {
      ALKScriptToken keyword = Previous();
      ALKScriptToken typeName = Consume(ALKScriptTokenType.Identifier, "Expect a type name after 'new'.");

      var typeArguments = new List<TypeNode>();

      if (Match(ALKScriptTokenType.Less))
      {
        do
        {
          typeArguments.Add(ParseType());
        } while (Match(ALKScriptTokenType.Comma));

        Consume(ALKScriptTokenType.Greater, "Expect '>' after type arguments.");
      }

      Consume(ALKScriptTokenType.LeftParen, "Expect '(' after type name in 'new' expression.");

      var arguments = new List<Expr>();

      if (!Check(ALKScriptTokenType.RightParen))
      {
        do
        {
          arguments.Add(ParseExpression());
        } while (Match(ALKScriptTokenType.Comma));
      }

      Consume(ALKScriptTokenType.RightParen, "Expect ')' after constructor arguments.");

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

    #region Token stream helpers

    private bool Match(params ALKScriptTokenType[] types)
    {
      foreach (ALKScriptTokenType type in types)
      {
        if (Check(type))
        {
          Advance();
          return true;
        }
      }

      return false;
    }

    private bool Check(ALKScriptTokenType type)
    {
      if (IsAtEnd())
      {
        return false;
      }

      return Peek().Type == type;
    }

    private bool CheckNext(ALKScriptTokenType type)
    {
      if (_current + 1 >= _tokens.Count)
      {
        return false;
      }

      return _tokens[_current + 1].Type == type;
    }

    private ALKScriptToken Advance()
    {
      if (!IsAtEnd())
      {
        _current++;
      }

      return Previous();
    }

    private bool IsAtEnd()
    {
      return Peek().Type == ALKScriptTokenType.EndOfFile;
    }

    private ALKScriptToken Peek()
    {
      return _tokens[_current];
    }

    private ALKScriptToken Previous()
    {
      return _tokens[_current - 1];
    }

    private ALKScriptToken Consume(ALKScriptTokenType type, string errorMessage)
    {
      if (Check(type))
      {
        return Advance();
      }

      throw Error(Peek(), errorMessage);
    }

    private static ParseException Error(ALKScriptToken token, string message)
    {
      return new ParseException(token, message);
    }

    #endregion
  }
}
