using ALKScript.Interpreter.Common.Ast;

namespace Tests.ALKScript.Interpreter.Parser;

public class BreakContinueTests : ParserTestBase
{
  // ── break ────────────────────────────────────────────────────────────────

  [Fact]
  public void Parse_BreakInsideWhileBody_ProducesBreakStmt()
  {
    var program = Parse("while (true) { break; }");

    var stmtDecl = Assert.IsType<StatementDecl>(Assert.Single(program.Declarations));
    var whileStmt = Assert.IsType<WhileStmt>(stmtDecl.Statement);
    var block = Assert.IsType<BlockStmt>(whileStmt.Body);
    var inner = Assert.IsType<StatementDecl>(Assert.Single(block.Statements));
    var breakStmt = Assert.IsType<BreakStmt>(inner.Statement);

    Assert.Equal("break", breakStmt.Keyword.Lexeme);
  }

  [Fact]
  public void Parse_BreakInsideForBody_ProducesBreakStmt()
  {
    var program = Parse("for (var i = 0; i < 10; i = i + 1) { break; }");

    var stmtDecl = Assert.IsType<StatementDecl>(Assert.Single(program.Declarations));
    var forStmt = Assert.IsType<ForStmt>(stmtDecl.Statement);
    var block = Assert.IsType<BlockStmt>(forStmt.Body);
    var inner = Assert.IsType<StatementDecl>(Assert.Single(block.Statements));
    Assert.IsType<BreakStmt>(inner.Statement);
  }

  [Fact]
  public void Parse_BreakWithoutSemicolon_ThrowsParseException()
  {
    Assert.ThrowsAny<Exception>(() => Parse("while (true) { break }"));
  }

  // ── continue ─────────────────────────────────────────────────────────────

  [Fact]
  public void Parse_ContinueInsideWhileBody_ProducesContinueStmt()
  {
    var program = Parse("while (true) { continue; }");

    var stmtDecl = Assert.IsType<StatementDecl>(Assert.Single(program.Declarations));
    var whileStmt = Assert.IsType<WhileStmt>(stmtDecl.Statement);
    var block = Assert.IsType<BlockStmt>(whileStmt.Body);
    var inner = Assert.IsType<StatementDecl>(Assert.Single(block.Statements));
    var continueStmt = Assert.IsType<ContinueStmt>(inner.Statement);

    Assert.Equal("continue", continueStmt.Keyword.Lexeme);
  }

  [Fact]
  public void Parse_ContinueInsideForBody_ProducesContinueStmt()
  {
    var program = Parse("for (var i = 0; i < 10; i = i + 1) { continue; }");

    var stmtDecl = Assert.IsType<StatementDecl>(Assert.Single(program.Declarations));
    var forStmt = Assert.IsType<ForStmt>(stmtDecl.Statement);
    var block = Assert.IsType<BlockStmt>(forStmt.Body);
    var inner = Assert.IsType<StatementDecl>(Assert.Single(block.Statements));
    Assert.IsType<ContinueStmt>(inner.Statement);
  }

  [Fact]
  public void Parse_ContinueWithoutSemicolon_ThrowsParseException()
  {
    Assert.ThrowsAny<Exception>(() => Parse("while (true) { continue }"));
  }

  // ── nesting ───────────────────────────────────────────────────────────────

  [Fact]
  public void Parse_BreakAndContinueInSameLoop_BothParsedInOrder()
  {
    // if (cond) continue; else break; — two control-flow stmts in one body
    var program = Parse(
      "while (true) {" +
      "  if (true) { continue; } else { break; }" +
      "}");

    var stmtDecl = Assert.IsType<StatementDecl>(Assert.Single(program.Declarations));
    var whileStmt = Assert.IsType<WhileStmt>(stmtDecl.Statement);
    var block = Assert.IsType<BlockStmt>(whileStmt.Body);
    var ifDecl = Assert.IsType<StatementDecl>(Assert.Single(block.Statements));
    var ifStmt = Assert.IsType<IfStmt>(ifDecl.Statement);

    var thenDecl = Assert.IsType<StatementDecl>(Assert.IsType<BlockStmt>(ifStmt.ThenBranch).Statements.Single());
    Assert.IsType<ContinueStmt>(thenDecl.Statement);

    var elseDecl = Assert.IsType<StatementDecl>(Assert.IsType<BlockStmt>(ifStmt.ElseBranch!).Statements.Single());
    Assert.IsType<BreakStmt>(elseDecl.Statement);
  }

  [Fact]
  public void Parse_BreakInNestedLoop_ParsesWithoutError()
  {
    // Validates that break in an inner loop parses correctly — scoping is a
    // runtime/semantic concern, not a parse-time one.
    var program = Parse(
      "for (var i = 0; i < 3; i = i + 1) {" +
      "  for (var j = 0; j < 3; j = j + 1) {" +
      "    break;" +
      "  }" +
      "}");

    Assert.Single(program.Declarations);
  }
}
