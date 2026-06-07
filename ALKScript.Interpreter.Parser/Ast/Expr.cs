using System.Collections.Generic;
using ALKScript.Interpreter.Common;

namespace ALKScript.Interpreter.Parser.Ast
{
  /// <summary>
  /// Base type for all expression AST nodes. Corresponds to the "expression"
  /// production and its descendants in the language grammar (§3, §6).
  /// </summary>
  public abstract class Expr
  {
  }

  /// <summary>A literal value: number, string, "true", "false", or "null".</summary>
  public class LiteralExpr : Expr
  {
    public ALKScriptToken Token { get; }
    public object? Value { get; }

    public LiteralExpr(ALKScriptToken token, object? value)
    {
      Token = token;
      Value = value;
    }
  }

  /// <summary>A bare identifier reference, e.g. "x" or "add".</summary>
  public class IdentifierExpr : Expr
  {
    public ALKScriptToken Name { get; }

    public IdentifierExpr(ALKScriptToken name)
    {
      Name = name;
    }
  }

  /// <summary>The "this" keyword, referring to the current instance.</summary>
  public class ThisExpr : Expr
  {
    public ALKScriptToken Keyword { get; }

    public ThisExpr(ALKScriptToken keyword)
    {
      Keyword = keyword;
    }
  }

  /// <summary>The "base" keyword, referring to the superclass instance/constructor.</summary>
  public class BaseExpr : Expr
  {
    public ALKScriptToken Keyword { get; }

    public BaseExpr(ALKScriptToken keyword)
    {
      Keyword = keyword;
    }
  }

  /// <summary>A parenthesized sub-expression: "(" expression ")".</summary>
  public class GroupingExpr : Expr
  {
    public Expr Expression { get; }

    public GroupingExpr(Expr expression)
    {
      Expression = expression;
    }
  }

  /// <summary>An array literal: "[" arguments? "]".</summary>
  public class ArrayLiteralExpr : Expr
  {
    public IReadOnlyList<Expr> Elements { get; }

    public ArrayLiteralExpr(IReadOnlyList<Expr> elements)
    {
      Elements = elements;
    }
  }

  /// <summary>An assignment expression: "IDENTIFIER" "=" assignment.</summary>
  public class AssignmentExpr : Expr
  {
    public Expr Target { get; }
    public Expr Value { get; }

    public AssignmentExpr(Expr target, Expr value)
    {
      Target = target;
      Value = value;
    }
  }

  /// <summary>
  /// A binary operator expression covering logical (||, &amp;&amp;), equality
  /// (==, !=), comparison (&lt;, &lt;=, &gt;, &gt;=), and arithmetic
  /// (+, -, *, /, %) operators.
  /// </summary>
  public class BinaryExpr : Expr
  {
    public Expr Left { get; }
    public ALKScriptToken Operator { get; }
    public Expr Right { get; }

    public BinaryExpr(Expr left, ALKScriptToken op, Expr right)
    {
      Left = left;
      Operator = op;
      Right = right;
    }
  }

  /// <summary>A unary prefix expression: "!" | "-" applied to its operand.</summary>
  public class UnaryExpr : Expr
  {
    public ALKScriptToken Operator { get; }
    public Expr Operand { get; }

    public UnaryExpr(ALKScriptToken op, Expr operand)
    {
      Operator = op;
      Operand = operand;
    }
  }

  /// <summary>An "await" expression: "await" unary.</summary>
  public class AwaitExpr : Expr
  {
    public ALKScriptToken Keyword { get; }
    public Expr Operand { get; }

    public AwaitExpr(ALKScriptToken keyword, Expr operand)
    {
      Keyword = keyword;
      Operand = operand;
    }
  }

  /// <summary>A function/method call: callee "(" arguments? ")".</summary>
  public class CallExpr : Expr
  {
    public Expr Callee { get; }
    public ALKScriptToken ClosingParen { get; }
    public IReadOnlyList<Expr> Arguments { get; }

    public CallExpr(Expr callee, ALKScriptToken closingParen, IReadOnlyList<Expr> arguments)
    {
      Callee = callee;
      ClosingParen = closingParen;
      Arguments = arguments;
    }
  }

  /// <summary>A member-access expression: object "." IDENTIFIER.</summary>
  public class GetExpr : Expr
  {
    public Expr Target { get; }
    public ALKScriptToken Name { get; }

    public GetExpr(Expr target, ALKScriptToken name)
    {
      Target = target;
      Name = name;
    }
  }

  /// <summary>An indexing expression: object "[" expression "]".</summary>
  public class IndexExpr : Expr
  {
    public Expr Target { get; }
    public Expr Index { get; }
    public ALKScriptToken ClosingBracket { get; }

    public IndexExpr(Expr target, Expr index, ALKScriptToken closingBracket)
    {
      Target = target;
      Index = index;
      ClosingBracket = closingBracket;
    }
  }

  /// <summary>An object-instantiation expression: "new" IDENTIFIER ("&lt;" type, ... "&gt;")? "(" arguments? ")".</summary>
  public class NewExpr : Expr
  {
    public ALKScriptToken Keyword { get; }
    public ALKScriptToken TypeName { get; }
    public IReadOnlyList<TypeNode> TypeArguments { get; }
    public IReadOnlyList<Expr> Arguments { get; }

    public NewExpr(ALKScriptToken keyword, ALKScriptToken typeName, IReadOnlyList<TypeNode> typeArguments, IReadOnlyList<Expr> arguments)
    {
      Keyword = keyword;
      TypeName = typeName;
      TypeArguments = typeArguments;
      Arguments = arguments;
    }
  }
}
