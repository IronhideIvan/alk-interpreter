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
        { ALKScriptTokenType.Lambda, "lambda" },
        { ALKScriptTokenType.Thunk, "thunk" },
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

      var program = new ProgramNode(imports, declarations);

      AwaitPositionValidator.Validate(program);

      return program;
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

      return new NamedImportsClause(ParseNamedSpecifierList());
    }

    /// <summary>Parses "{" importSpecifier ("," importSpecifier)* "}", shared by named imports and re-exports.</summary>
    private List<ImportSpecifier> ParseNamedSpecifierList()
    {
      _stream.Consume(ALKScriptTokenType.LeftBrace, "Expect '{' to begin a named-specifier list.");

      var specifiers = new List<ImportSpecifier>();

      if (!_stream.Check(ALKScriptTokenType.RightBrace))
      {
        do
        {
          specifiers.Add(ParseImportSpecifier());
        } while (_stream.Match(ALKScriptTokenType.Comma));
      }

      _stream.Consume(ALKScriptTokenType.RightBrace, "Expect '}' after named specifiers.");

      return specifiers;
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

      if (_stream.Check(ALKScriptTokenType.Interface))
      {
        return ParseInterfaceDecl();
      }

      if (_stream.Check(ALKScriptTokenType.Enum))
      {
        return ParseEnumDecl();
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

    private Decl ParseExportDecl()
    {
      // "export { Foo, Bar as Baz } from "./module";" re-exports named members
      // of another module under this module's export surface.
      if (_stream.Check(ALKScriptTokenType.LeftBrace))
      {
        List<ImportSpecifier> specifiers = ParseNamedSpecifierList();

        _stream.Consume(ALKScriptTokenType.From, "Expect 'from' after re-export specifiers.");
        ALKScriptToken source = _stream.Consume(ALKScriptTokenType.String, "Expect a module path string after 'from'.");
        _stream.Consume(ALKScriptTokenType.Semicolon, "Expect ';' after re-export declaration.");

        return new ReExportDecl(specifiers, source);
      }

      // "export" is only valid immediately before a class, interface, enum,
      // function, or variable declaration.
      if (CheckClassDeclStart())
      {
        return new ExportDecl(ParseClassDecl());
      }

      if (_stream.Check(ALKScriptTokenType.Interface))
      {
        return new ExportDecl(ParseInterfaceDecl());
      }

      if (_stream.Check(ALKScriptTokenType.Enum))
      {
        return new ExportDecl(ParseEnumDecl());
      }

      if (CheckFunctionDeclStart())
      {
        return new ExportDecl(ParseFunctionDecl());
      }

      if (TryParseVariableDecl(out VariableDecl? variableDecl))
      {
        return new ExportDecl(variableDecl!);
      }

      throw Error(_stream.Peek(), "Expect a class, interface, enum, function, or variable declaration after 'export'.");
    }

    private bool CheckClassDeclStart()
    {
      int offset = 0;

      if (_stream.CheckAhead(offset, ALKScriptTokenType.Native))
      {
        offset++;
      }

      if (_stream.CheckAhead(offset, ALKScriptTokenType.Abstract) || _stream.CheckAhead(offset, ALKScriptTokenType.Sealed))
      {
        offset++;
      }

      return _stream.CheckAhead(offset, ALKScriptTokenType.Class);
    }

    private ClassDecl ParseClassDecl()
    {
      bool isNative = _stream.Match(ALKScriptTokenType.Native);
      bool isAbstract = _stream.Match(ALKScriptTokenType.Abstract);
      bool isSealed = !isAbstract && _stream.Match(ALKScriptTokenType.Sealed);

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

      var interfaces = new List<ALKScriptToken>();

      if (_stream.Match(ALKScriptTokenType.Implements))
      {
        do
        {
          interfaces.Add(_stream.Consume(ALKScriptTokenType.Identifier, "Expect an interface name."));
        } while (_stream.Match(ALKScriptTokenType.Comma));
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

      return new ClassDecl(isAbstract, name, typeParameters, superclassName, superclassTypeArguments, members, isNative, isSealed, interfaces);
    }

    private InterfaceDecl ParseInterfaceDecl()
    {
      _stream.Consume(ALKScriptTokenType.Interface, "Expect 'interface'.");

      ALKScriptToken name = _stream.Consume(ALKScriptTokenType.Identifier, "Expect an interface name.");
      List<string> typeParameters = ParseOptionalTypeParameters();

      var extends = new List<ALKScriptToken>();

      if (_stream.Match(ALKScriptTokenType.Extends))
      {
        do
        {
          extends.Add(_stream.Consume(ALKScriptTokenType.Identifier, "Expect an interface name after 'extends'."));
        } while (_stream.Match(ALKScriptTokenType.Comma));
      }

      _stream.Consume(ALKScriptTokenType.LeftBrace, "Expect '{' before interface body.");

      var methods = new List<InterfaceMethodSignature>();
      var properties = new List<InterfacePropertySignature>();

      while (!_stream.Check(ALKScriptTokenType.RightBrace) && !_stream.IsAtEnd())
      {
        // Interface property: "property" type IDENTIFIER "{" ("get" ";")? ("set" ";")? "}"
        if (_stream.Match(ALKScriptTokenType.Property))
        {
          TypeNode propType = ParseType();
          ALKScriptToken propName = _stream.Consume(ALKScriptTokenType.Identifier, "Expect a property name.");
          _stream.Consume(ALKScriptTokenType.LeftBrace, "Expect '{' after interface property name.");

          bool hasGet = false, hasSet = false;
          while (!_stream.Check(ALKScriptTokenType.RightBrace) && !_stream.IsAtEnd())
          {
            if (IsContextualKeyword("get")) { _stream.Advance(); _stream.Consume(ALKScriptTokenType.Semicolon, "Expect ';' after 'get' in interface property."); hasGet = true; }
            else if (IsContextualKeyword("set")) { _stream.Advance(); _stream.Consume(ALKScriptTokenType.Semicolon, "Expect ';' after 'set' in interface property."); hasSet = true; }
            else throw Error(_stream.Peek(), "Expect 'get' or 'set' in interface property.");
          }
          _stream.Consume(ALKScriptTokenType.RightBrace, "Expect '}' after interface property.");
          properties.Add(new InterfacePropertySignature(propType, propName, hasGet, hasSet));
          continue;
        }

        List<string> methodTypeParameters = ParseOptionalTypeParameters();
        TypeNode returnType = ParseType();
        ALKScriptToken methodName = _stream.Consume(ALKScriptTokenType.Identifier, "Expect a method name.");
        List<Parameter> parameters = ParseParameterList();
        _stream.Consume(ALKScriptTokenType.Semicolon, "Expect ';' after interface method signature.");

        methods.Add(new InterfaceMethodSignature(methodTypeParameters, returnType, methodName, parameters));
      }

      _stream.Consume(ALKScriptTokenType.RightBrace, "Expect '}' after interface body.");

      return new InterfaceDecl(name, typeParameters, extends, methods, properties);
    }

    private EnumDecl ParseEnumDecl()
    {
      _stream.Consume(ALKScriptTokenType.Enum, "Expect 'enum'.");

      ALKScriptToken name = _stream.Consume(ALKScriptTokenType.Identifier, "Expect an enum name.");

      _stream.Consume(ALKScriptTokenType.LeftBrace, "Expect '{' before enum body.");

      var members = new List<EnumMember>();

      while (!_stream.Check(ALKScriptTokenType.RightBrace) && !_stream.IsAtEnd())
      {
        ALKScriptToken memberName = _stream.Consume(ALKScriptTokenType.Identifier, "Expect an enum member name.");

        long? explicitValue = null;

        if (_stream.Match(ALKScriptTokenType.Equal))
        {
          bool negative = _stream.Match(ALKScriptTokenType.Minus);
          ALKScriptToken numberToken = _stream.Consume(ALKScriptTokenType.Number, "Expect an integer constant for an enum member value.");

          if (!long.TryParse(numberToken.Lexeme, out long parsedValue))
          {
            throw Error(numberToken, $"Expect an integer constant for enum member '{memberName.Lexeme}'.");
          }

          explicitValue = negative ? -parsedValue : parsedValue;
        }

        members.Add(new EnumMember(memberName, explicitValue));

        if (!_stream.Match(ALKScriptTokenType.Comma))
        {
          break;
        }
      }

      _stream.Consume(ALKScriptTokenType.RightBrace, "Expect '}' after enum body.");

      return new EnumDecl(name, members);
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

      ALKScriptToken? staticKeyword = _stream.Check(ALKScriptTokenType.Static) ? _stream.Peek() : null;
      bool isStatic = _stream.Match(ALKScriptTokenType.Static);

      ALKScriptToken? readonlyKeyword = _stream.Check(ALKScriptTokenType.Readonly) ? _stream.Peek() : null;
      bool isReadonly = _stream.Match(ALKScriptTokenType.Readonly);

      OverrideModifier overrideModifier = ParseOptionalOverrideModifier();

      if (isStatic && overrideModifier != OverrideModifier.None)
      {
        throw Error(staticKeyword!, "'static' members cannot be 'virtual', 'abstract', or 'override'.");
      }

      if (isReadonly && (overrideModifier != OverrideModifier.None || _stream.Check(ALKScriptTokenType.Native) || _stream.Check(ALKScriptTokenType.Function) || _stream.Check(ALKScriptTokenType.Property)))
      {
        throw Error(readonlyKeyword!, "'readonly' is only valid on field declarations.");
      }

      if (isStatic && isReadonly)
      {
        throw Error(readonlyKeyword!, "'readonly' is not supported on 'static' fields.");
      }

      // Operator overload: accessModifier? "static" "operator" returnType op "(" params ")" block
      if (isStatic && _stream.Match(ALKScriptTokenType.Operator))
      {
        if (overrideModifier != OverrideModifier.None)
          throw Error(_stream.Previous(), "Operator overloads cannot have override modifiers.");

        TypeNode opReturnType = ParseType();
        ALKScriptToken opToken = ParseOperatorToken();
        List<Parameter> opParams = ParseParameterList();

        if (opParams.Count < 1 || opParams.Count > 2)
          throw Error(opToken, "Operator overloads must have exactly 1 (unary) or 2 (binary) parameters.");

        BlockStmt opBody = ParseBlock();
        return new OperatorOverloadDecl(accessModifier, opToken, opReturnType, opParams, opBody);
      }

      if (!isStatic && _stream.Check(ALKScriptTokenType.Operator))
        throw Error(_stream.Peek(), "Operator overload methods must be 'static'.");

      // Property: accessModifier? "static"? overrideModifier? "property" type IDENTIFIER "{" ... "}"
      if (_stream.Match(ALKScriptTokenType.Property))
      {
        if (isReadonly)
          throw Error(readonlyKeyword!, "'readonly' cannot be combined with 'property'.");
        TypeNode propType = ParseType();
        ALKScriptToken propName = _stream.Consume(ALKScriptTokenType.Identifier, "Expect a property name.");
        _stream.Consume(ALKScriptTokenType.LeftBrace, "Expect '{' after property name.");

        bool hasGetter = false;
        BlockStmt? getterBody = null;
        bool hasSetter = false;
        BlockStmt? setterBody = null;

        while (!_stream.Check(ALKScriptTokenType.RightBrace) && !_stream.IsAtEnd())
        {
          if (IsContextualKeyword("get"))
          {
            _stream.Advance();
            hasGetter = true;
            if (_stream.Match(ALKScriptTokenType.Semicolon))
            {
              getterBody = null; // auto-property
            }
            else
            {
              getterBody = ParseBlock();
            }
          }
          else if (IsContextualKeyword("set"))
          {
            _stream.Advance();
            hasSetter = true;
            if (_stream.Match(ALKScriptTokenType.Semicolon))
            {
              setterBody = null; // auto-property
            }
            else
            {
              setterBody = ParseBlock();
            }
          }
          else
          {
            throw Error(_stream.Peek(), "Expect 'get' or 'set' in property body.");
          }
        }

        _stream.Consume(ALKScriptTokenType.RightBrace, "Expect '}' after property body.");
        return new PropertyDecl(accessModifier, propName, propType, isStatic, overrideModifier, hasGetter, getterBody, hasSetter, setterBody);
      }

      bool isNative = _stream.Match(ALKScriptTokenType.Native);

      if (overrideModifier != OverrideModifier.None || isNative || _stream.Check(ALKScriptTokenType.Function))
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

        return new MethodDecl(accessModifier, overrideModifier, isNative, typeParameters, returnType, methodName, parameters, body, isStatic);
      }

      // Field: accessModifier? "static"? "readonly"? ("var" | type) IDENTIFIER ("=" expression)? ";"
      TypeNode? fieldType = _stream.Match(ALKScriptTokenType.Var) ? null : ParseType();
      ALKScriptToken fieldName = _stream.Consume(ALKScriptTokenType.Identifier, "Expect a field name.");

      Expr? fieldInitializer = null;

      if (_stream.Match(ALKScriptTokenType.Equal))
      {
        fieldInitializer = ParseExpression();
      }

      _stream.Consume(ALKScriptTokenType.Semicolon, "Expect ';' after field declaration.");

      return new FieldDecl(accessModifier, fieldType, fieldName, fieldInitializer, isStatic, isReadonly);
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

      return _stream.CheckAhead(offset, ALKScriptTokenType.Function);
    }

    private FunctionDecl ParseFunctionDecl()
    {
      bool isNative = _stream.Match(ALKScriptTokenType.Native);
      _stream.Consume(ALKScriptTokenType.Function, "Expect 'function'.");

      List<string> typeParameters = ParseOptionalTypeParameters();
      TypeNode returnType = ParseType();
      ALKScriptToken name = _stream.Consume(ALKScriptTokenType.Identifier, "Expect a function name.");
      List<Parameter> parameters = ParseParameterList();

      BlockStmt? body = ParseFunctionOrMethodBody(isNative, "function");

      return new FunctionDecl(isNative, typeParameters, returnType, name, parameters, body);
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
    /// Attempts to parse a variable declaration "const"? ("var" | type)
    /// IDENTIFIER ("=" expression)? ";" starting at the current position. If
    /// the token sequence does not match (e.g. it is actually an expression
    /// statement such as a call or assignment), the parser position is
    /// restored and false is returned. A leading "const" requires an
    /// initializer (a parse-time error otherwise).
    /// </summary>
    private bool TryParseVariableDecl(out VariableDecl? declaration)
    {
      int checkpoint = _stream.Position;

      bool isConst = _stream.Match(ALKScriptTokenType.Const);

      bool isVar = _stream.Match(ALKScriptTokenType.Var);
      TypeNode? type = null;

      if (!isVar)
      {
        if (!CanStartType())
        {
          _stream.Position = checkpoint;
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

      if (isConst && initializer == null)
      {
        throw Error(name, "A 'const' declaration requires an initializer.");
      }

      declaration = new VariableDecl(type, name, initializer, isConst);
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
      else if (token.Type == ALKScriptTokenType.Map)
      {
        name = "map";
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
      else if (name == "lambda")
      {
        // Bare "lambda" (no type arguments) is equivalent to "lambda<void>" —
        // a callable taking no parameters and returning nothing.
        typeArguments.Add(new TypeNode("void", System.Array.Empty<TypeNode>(), 0, false));
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
            ALKScriptTokenType.PercentEqual,
            ALKScriptTokenType.AmpEqual, ALKScriptTokenType.PipeEqual, ALKScriptTokenType.CaretEqual,
            ALKScriptTokenType.LessLessEqual, ALKScriptTokenType.GreaterGreaterEqual))
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
      Expr expr = ParseBitwiseOr();

      while (_stream.Match(ALKScriptTokenType.AmpAmp))
      {
        ALKScriptToken op = _stream.Previous();
        Expr right = ParseBitwiseOr();
        expr = new BinaryExpr(expr, op, right);
      }

      return expr;
    }

    private Expr ParseBitwiseOr()
    {
      Expr expr = ParseBitwiseXor();

      while (_stream.Match(ALKScriptTokenType.Pipe))
      {
        ALKScriptToken op = _stream.Previous();
        Expr right = ParseBitwiseXor();
        expr = new BinaryExpr(expr, op, right);
      }

      return expr;
    }

    private Expr ParseBitwiseXor()
    {
      Expr expr = ParseBitwiseAnd();

      while (_stream.Match(ALKScriptTokenType.Caret))
      {
        ALKScriptToken op = _stream.Previous();
        Expr right = ParseBitwiseAnd();
        expr = new BinaryExpr(expr, op, right);
      }

      return expr;
    }

    private Expr ParseBitwiseAnd()
    {
      Expr expr = ParseEquality();

      while (_stream.Match(ALKScriptTokenType.Amp))
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
      Expr expr = ParseTypeTest();

      while (_stream.Match(ALKScriptTokenType.Less, ALKScriptTokenType.LessEqual, ALKScriptTokenType.Greater, ALKScriptTokenType.GreaterEqual))
      {
        ALKScriptToken op = _stream.Previous();
        Expr right = ParseTypeTest();
        expr = new BinaryExpr(expr, op, right);
      }

      return expr;
    }

    private Expr ParseTypeTest()
    {
      Expr expr = ParseShift();

      while (_stream.Check(ALKScriptTokenType.Is) || _stream.Check(ALKScriptTokenType.As))
      {
        ALKScriptToken keyword = _stream.Advance();
        TypeNode type = ParseType();

        expr = keyword.Type == ALKScriptTokenType.Is
          ? new TypeTestExpr(expr, keyword, type)
          : new TypeCastExpr(expr, keyword, type);
      }

      return expr;
    }

    private Expr ParseShift()
    {
      Expr expr = ParseTerm();

      while (_stream.Match(ALKScriptTokenType.LessLess, ALKScriptTokenType.GreaterGreater))
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

      if (_stream.Match(ALKScriptTokenType.Bang, ALKScriptTokenType.Minus, ALKScriptTokenType.Tilde))
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

      if (_stream.Match(ALKScriptTokenType.Typeof))
      {
        ALKScriptToken keyword = _stream.Previous();
        Expr operand = ParseUnary();
        return new TypeofExpr(keyword, operand);
      }

      if (CheckNumericCastStart())
      {
        _stream.Consume(ALKScriptTokenType.LeftParen, "Expect '('.");
        ALKScriptToken typeToken = _stream.Advance();
        _stream.Consume(ALKScriptTokenType.RightParen, "Expect ')' after cast type.");
        Expr operand = ParseUnary();
        return new CastExpr(typeToken, PrimitiveTypeNames[typeToken.Type], operand);
      }

      return ParseCall();
    }

    /// <summary>
    /// Looks ahead for "(" ("int" | "long" | "float") ")" — a numeric
    /// conversion cast. Limited to these three primitive types: they're not
    /// otherwise valid as a parenthesized expression's sole content, so this
    /// can't be confused with a grouping expression like "(x)".
    /// </summary>
    private bool CheckNumericCastStart()
    {
      return _stream.CheckAhead(0, ALKScriptTokenType.LeftParen)
        && (_stream.CheckAhead(1, ALKScriptTokenType.IntKeyword) || _stream.CheckAhead(1, ALKScriptTokenType.LongKeyword) || _stream.CheckAhead(1, ALKScriptTokenType.FloatKeyword))
        && _stream.CheckAhead(2, ALKScriptTokenType.RightParen);
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
          ALKScriptToken name = ConsumeMemberName("Expect a property or method name after '.'.");
          expr = new GetExpr(expr, name);
        }
        else if (_stream.Match(ALKScriptTokenType.QuestionDot))
        {
          ALKScriptToken name = ConsumeMemberName("Expect a property or method name after '?.'.");
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
      if (TryParseLambda(out Expr? lambdaExpr))
      {
        return lambdaExpr!;
      }

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

      if (_stream.Check(ALKScriptTokenType.InterpolatedStringStart) || _stream.Check(ALKScriptTokenType.InterpolatedStringEnd))
      {
        return ParseInterpolatedString();
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

    private Expr ParseInterpolatedString()
    {
      var parts = new List<string>();
      var expressions = new List<Expr>();

      ALKScriptToken first = _stream.Advance();
      parts.Add(first.Lexeme);

      if (first.Type == ALKScriptTokenType.InterpolatedStringEnd)
      {
        return new InterpolatedStringExpr(first, parts, expressions);
      }

      while (true)
      {
        expressions.Add(ParseExpression());

        ALKScriptToken segment = _stream.Advance();

        if (segment.Type == ALKScriptTokenType.InterpolatedStringMid)
        {
          parts.Add(segment.Lexeme);
          continue;
        }

        if (segment.Type == ALKScriptTokenType.InterpolatedStringEnd)
        {
          parts.Add(segment.Lexeme);
          break;
        }

        throw Error(segment, "Expect '}' to close an interpolated expression in a template string.");
      }

      return new InterpolatedStringExpr(first, parts, expressions);
    }

    /// <summary>
    /// Attempts to parse a lambda expression: <c>"async"? type "(" parameters? ")" "=&gt;" block</c>.
    /// Lambdas begin with a type (the return type), which overlaps with both
    /// generic-type-argument parsing (e.g. "a &lt; b &gt; c") and ordinary
    /// calls (e.g. "foo(x)"), so this speculatively parses the
    /// return-type/parameter-list/"=&gt;" prefix and restores the parser
    /// position if it doesn't match — leaving the expression to be parsed
    /// normally (e.g. as a call or comparison).
    /// </summary>
    private bool TryParseLambda(out Expr? lambda)
    {
      int checkpoint = _stream.Position;

      if (!CanStartType())
      {
        _stream.Position = checkpoint;
        lambda = null;
        return false;
      }

      TypeNode returnType;
      List<Parameter> parameters;

      try
      {
        returnType = ParseType();

        if (!_stream.Check(ALKScriptTokenType.LeftParen))
        {
          _stream.Position = checkpoint;
          lambda = null;
          return false;
        }

        parameters = ParseParameterList();

        if (!_stream.Match(ALKScriptTokenType.EqualGreater))
        {
          _stream.Position = checkpoint;
          lambda = null;
          return false;
        }
      }
      catch (ParseException)
      {
        _stream.Position = checkpoint;
        lambda = null;
        return false;
      }

      ALKScriptToken arrow = _stream.Previous();
      BlockStmt body = ParseBlock();

      lambda = new LambdaExpr(arrow, returnType, parameters, body);
      return true;
    }

    private Expr ParseNewExpression()
    {
      ALKScriptToken keyword = _stream.Previous();

      // new map<K, V> { key: value, ... }
      if (_stream.Match(ALKScriptTokenType.Map))
      {
        _stream.Consume(ALKScriptTokenType.Less, "Expect '<' after 'map' in map literal.");
        TypeNode keyType = ParseType();
        _stream.Consume(ALKScriptTokenType.Comma, "Expect ',' between map key and value types.");
        TypeNode valueType = ParseType();
        _stream.Consume(ALKScriptTokenType.Greater, "Expect '>' after map type arguments.");
        _stream.Consume(ALKScriptTokenType.LeftBrace, "Expect '{' to begin map literal.");

        var entries = new List<(Expr, Expr)>();

        while (!_stream.Check(ALKScriptTokenType.RightBrace) && !_stream.IsAtEnd())
        {
          Expr entryKey = ParseExpression();
          _stream.Consume(ALKScriptTokenType.Colon, "Expect ':' between map key and value.");
          Expr entryValue = ParseExpression();
          entries.Add((entryKey, entryValue));

          if (!_stream.Match(ALKScriptTokenType.Comma))
          {
            break;
          }
        }

        _stream.Consume(ALKScriptTokenType.RightBrace, "Expect '}' after map entries.");
        return new MapLiteralExpr(keyword, keyType, valueType, entries);
      }

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

    private static readonly System.Collections.Generic.HashSet<ALKScriptTokenType> OverloadableOperators =
      new System.Collections.Generic.HashSet<ALKScriptTokenType>
      {
        ALKScriptTokenType.Plus, ALKScriptTokenType.Minus,
        ALKScriptTokenType.Star, ALKScriptTokenType.Slash, ALKScriptTokenType.Percent,
        ALKScriptTokenType.EqualEqual, ALKScriptTokenType.BangEqual,
        ALKScriptTokenType.Less, ALKScriptTokenType.LessEqual,
        ALKScriptTokenType.Greater, ALKScriptTokenType.GreaterEqual,
      };

    private ALKScriptToken ParseOperatorToken()
    {
      var token = _stream.Peek();
      if (!OverloadableOperators.Contains(token.Type))
        throw Error(token, $"'{token.Lexeme}' is not an overloadable operator. Allowed: +, -, *, /, %, ==, !=, <, <=, >, >=.");
      _stream.Advance();
      return token;
    }

    /// <summary>
    /// Returns true if the current token is an identifier with the given lexeme
    /// (used for contextual keywords like "get" and "set").
    /// </summary>
    private bool IsContextualKeyword(string keyword)
      => _stream.Check(ALKScriptTokenType.Identifier) && _stream.Peek().Lexeme == keyword;

    /// <summary>
    /// Consumes the next token as a member name (after '.' or '?.').
    /// Accepts identifiers and any keyword that can be used as a member name
    /// (e.g. array's "map" method is valid even after "map" became a keyword).
    /// </summary>
    private ALKScriptToken ConsumeMemberName(string errorMessage)
    {
      var token = _stream.Peek();
      if (token.Type == ALKScriptTokenType.EndOfFile)
        throw Error(token, errorMessage);
      // Accept identifiers and keywords (contextual use as member name)
      _stream.Advance();
      return new ALKScriptToken(ALKScriptTokenType.Identifier, token.Lexeme, token.Line, token.Column);
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
