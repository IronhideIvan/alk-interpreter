using System.Collections.Generic;
using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Common.Token;
using ALKScript.Interpreter.Evaluator;

namespace Tests.ALKScript.Interpreter.Evaluator.Unit;

public class AstTokenLocatorTests
{
  [Fact]
  public void Of_Statement_ReturnsTheStatementsLeadingToken()
  {
    var returnKeyword = Nodes.Token(ALKScriptTokenType.Identifier, "return");
    Assert.Same(returnKeyword, AstTokenLocator.Of(new ReturnStmt(returnKeyword, null)));

    var throwKeyword = Nodes.Token(ALKScriptTokenType.Identifier, "throw");
    Assert.Same(throwKeyword, AstTokenLocator.Of(new ThrowStmt(throwKeyword, Nodes.Literal(1L))));

    var variableName = Nodes.Identifier("x");
    Assert.Same(variableName, AstTokenLocator.Of(new VariableDecl(null, variableName, null)));

    var className = Nodes.Identifier("Foo");
    Assert.Same(className, AstTokenLocator.Of(new ClassDecl(false, className, System.Array.Empty<string>(), null, System.Array.Empty<TypeNode>(), System.Array.Empty<MemberDecl>())));
  }

  [Fact]
  public void Of_StatementWithoutADedicatedToken_ReturnsEndOfFile()
  {
    var located = AstTokenLocator.Of(new ExpressionStmt(Nodes.Literal(1L)));

    Assert.Equal(ALKScriptTokenType.EndOfFile, located.Type);
  }

  [Fact]
  public void Of_Expression_ReturnsTheExpressionsReportingToken()
  {
    var literalToken = Nodes.Token(ALKScriptTokenType.Number, "1");
    Assert.Same(literalToken, AstTokenLocator.Of(new LiteralExpr(literalToken, 1L)));

    var name = Nodes.Identifier("x");
    Assert.Same(name, AstTokenLocator.Of(new IdentifierExpr(name)));

    var thisKeyword = Nodes.Token(ALKScriptTokenType.Identifier, "this");
    Assert.Same(thisKeyword, AstTokenLocator.Of(new ThisExpr(thisKeyword)));

    var baseKeyword = Nodes.Token(ALKScriptTokenType.Identifier, "base");
    Assert.Same(baseKeyword, AstTokenLocator.Of(new BaseExpr(baseKeyword)));

    var op = Nodes.Operator(ALKScriptTokenType.Plus, "+");
    Assert.Same(op, AstTokenLocator.Of(new BinaryExpr(Nodes.Literal(1L), op, Nodes.Literal(2L))));

    var bang = Nodes.Operator(ALKScriptTokenType.Bang, "!");
    Assert.Same(bang, AstTokenLocator.Of(new UnaryExpr(bang, Nodes.Literal(true))));

    var closingParen = Nodes.Token(ALKScriptTokenType.RightParen, ")");
    Assert.Same(closingParen, AstTokenLocator.Of(new CallExpr(Nodes.Ident("f"), closingParen, System.Array.Empty<Expr>())));

    var propertyName = Nodes.Identifier("prop");
    Assert.Same(propertyName, AstTokenLocator.Of(new GetExpr(Nodes.Ident("x"), propertyName)));

    var closingBracket = Nodes.Token(ALKScriptTokenType.RightBracket, "]");
    Assert.Same(closingBracket, AstTokenLocator.Of(new IndexExpr(Nodes.Ident("x"), Nodes.Literal(0L), closingBracket)));

    var newKeyword = Nodes.Token(ALKScriptTokenType.New, "new");
    Assert.Same(newKeyword, AstTokenLocator.Of(new NewExpr(newKeyword, Nodes.Identifier("Foo"), System.Array.Empty<TypeNode>(), System.Array.Empty<Expr>())));

    var awaitKeyword = Nodes.Token(ALKScriptTokenType.Identifier, "await");
    Assert.Same(awaitKeyword, AstTokenLocator.Of(new AwaitExpr(awaitKeyword, Nodes.Ident("x"))));
  }

  [Fact]
  public void Of_AssignmentExpression_DelegatesToItsTarget()
  {
    var name = Nodes.Identifier("x");
    var assignment = new AssignmentExpr(new IdentifierExpr(name), Nodes.Literal(1L));

    Assert.Same(name, AstTokenLocator.Of(assignment));
  }

  [Fact]
  public void Of_ExpressionWithoutADedicatedToken_ReturnsEndOfFile()
  {
    var located = AstTokenLocator.Of(new GroupingExpr(Nodes.Literal(1L)));

    Assert.Equal(ALKScriptTokenType.EndOfFile, located.Type);
  }
}
