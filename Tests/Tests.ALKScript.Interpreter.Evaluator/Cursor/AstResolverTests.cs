using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Evaluator.Cursor;

namespace Tests.ALKScript.Interpreter.Evaluator.Cursor;

/// <summary>
/// Step 2 coverage for the "Phase B" structural Capture/Restore plan (docs:
/// validated-nibbling-narwhal): <see cref="AstResolver"/>'s address table and
/// top-level resolution for non-generic <see cref="ClassDecl"/>/<see cref="FunctionDecl"/>
/// declarations in the entry module and in a global prelude
/// (docs/ASYNC_AWAIT_DESIGN.md Addendum 3).
/// </summary>
public class AstResolverTests : EvaluatorTestBase
{
  [Fact]
  public void BuildAddressTable_TopLevelClassAndFunction_ResolveByReferenceIdentity()
  {
    var source = "class Animal {}\nfunction int square(int x) { return x * x; }";

    var graph = LoadGraph(source);
    var table = AstResolver.BuildAddressTable(graph);

    var program = graph.EntryModule.Program;
    var classDecl = Assert.IsType<ClassDecl>(program.Declarations[0]);
    var functionDecl = Assert.IsType<FunctionDecl>(program.Declarations[1]);

    Assert.Equal("module:entry", table[classDecl].ModuleKey);
    Assert.Equal("Animal", table[classDecl].Path);

    Assert.Equal("module:entry", table[functionDecl].ModuleKey);
    Assert.Equal("square", table[functionDecl].Path);
  }

  [Fact]
  public void Resolve_TopLevelReference_RoundTripsToTheSameDeclaration()
  {
    var source = "class Animal {}\nfunction int square(int x) { return x * x; }";

    var graph = LoadGraph(source);
    var table = AstResolver.BuildAddressTable(graph);

    var program = graph.EntryModule.Program;
    var classDecl = Assert.IsType<ClassDecl>(program.Declarations[0]);
    var functionDecl = Assert.IsType<FunctionDecl>(program.Declarations[1]);

    Assert.Same(classDecl, AstResolver.ResolveTopLevel(graph, table[classDecl]));
    Assert.Same(functionDecl, AstResolver.ResolveTopLevel(graph, table[functionDecl]));
  }

  [Fact]
  public void BuildAddressTable_GlobalPreludeDeclarations_AddressedByPreludeIndex()
  {
    var source = "var unused = 0;";
    var preludeSource = "class Vector2 {}";

    var graph = LoadGraph(source, new[] { preludeSource });
    var table = AstResolver.BuildAddressTable(graph);

    var preludeClass = Assert.IsType<ClassDecl>(graph.GlobalPreludes[0].Declarations[0]);

    Assert.Equal("prelude:0", table[preludeClass].ModuleKey);
    Assert.Equal("Vector2", table[preludeClass].Path);
    Assert.Same(preludeClass, AstResolver.ResolveTopLevel(graph, table[preludeClass]));
  }

  [Fact]
  public void BuildAddressTable_ExportedDeclaration_UnwrapsExportDecl()
  {
    var source = "export class Animal {}";

    var graph = LoadGraph(source);
    var table = AstResolver.BuildAddressTable(graph);

    var exportDecl = Assert.IsType<ExportDecl>(graph.EntryModule.Program.Declarations[0]);
    var classDecl = Assert.IsType<ClassDecl>(exportDecl.Declaration);

    Assert.Equal("Animal", table[classDecl].Path);
    Assert.Same(classDecl, AstResolver.ResolveTopLevel(graph, table[classDecl]));
  }

  [Fact]
  public void Resolve_ClassMethod_ReturnsTheMethodDecl()
  {
    var source = "class Animal {\n  function string speak() { return \"...\"; }\n}";

    var graph = LoadGraph(source);
    var table = AstResolver.BuildAddressTable(graph);

    var classDecl = Assert.IsType<ClassDecl>(graph.EntryModule.Program.Declarations[0]);
    var methodDecl = Assert.IsType<MethodDecl>(classDecl.Members[0]);

    var methodRef = AstResolver.AddressOfMember(table[classDecl], "speak");
    Assert.Same(methodDecl, AstResolver.Resolve(graph, methodRef));
  }

  [Fact]
  public void Resolve_StaticField_ReturnsTheFieldDecl()
  {
    var source = "class Counter {\n  static var count = 0;\n}";

    var graph = LoadGraph(source);
    var table = AstResolver.BuildAddressTable(graph);

    var classDecl = Assert.IsType<ClassDecl>(graph.EntryModule.Program.Declarations[0]);
    var fieldDecl = Assert.IsType<FieldDecl>(classDecl.Members[0]);

    var fieldRef = AstResolver.AddressOfMember(table[classDecl], "count");
    Assert.Same(fieldDecl, AstResolver.Resolve(graph, fieldRef));
  }

  [Fact]
  public void Resolve_Constructor_ReturnsTheConstructorDecl()
  {
    var source = "class Animal {\n  new(string name) {}\n}";

    var graph = LoadGraph(source);
    var table = AstResolver.BuildAddressTable(graph);

    var classDecl = Assert.IsType<ClassDecl>(graph.EntryModule.Program.Declarations[0]);
    var ctorDecl = Assert.IsType<ConstructorDecl>(classDecl.Members[0]);

    var ctorRef = AstResolver.AddressOfMember(table[classDecl], AstResolver.ConstructorSegment);
    Assert.Same(ctorDecl, AstResolver.Resolve(graph, ctorRef));
  }

  [Fact]
  public void Resolve_TopLevelInterface_RoundTripsToTheSameDeclaration()
  {
    var source = "interface Shape {\n  float area();\n}";

    var graph = LoadGraph(source);
    var table = AstResolver.BuildAddressTable(graph);

    var interfaceDecl = Assert.IsType<InterfaceDecl>(graph.EntryModule.Program.Declarations[0]);

    Assert.Equal("Shape", table[interfaceDecl].Path);
    Assert.Same(interfaceDecl, AstResolver.ResolveTopLevel(graph, table[interfaceDecl]));
  }

  [Fact]
  public void Resolve_EnumMember_ReturnsTheEnumMember()
  {
    var source = "enum Color { Red, Green, Blue }";

    var graph = LoadGraph(source);
    var table = AstResolver.BuildAddressTable(graph);

    var enumDecl = Assert.IsType<EnumDecl>(graph.EntryModule.Program.Declarations[0]);
    var redMember = enumDecl.Members[0];

    var memberRef = AstResolver.AddressOfMember(table[enumDecl], "Red");
    Assert.Same(redMember, AstResolver.Resolve(graph, memberRef));
  }

  [Fact]
  public void Resolve_LambdaNestedInMethodBody_ReturnsTheLambdaExpr()
  {
    var source = "class Animal {\n  function int run() {\n    var f = int (int x) => { return x * 2; };\n    return f(1);\n  }\n}";

    var graph = LoadGraph(source);
    var table = AstResolver.BuildAddressTable(graph);

    var classDecl = Assert.IsType<ClassDecl>(graph.EntryModule.Program.Declarations[0]);
    var methodDecl = Assert.IsType<MethodDecl>(classDecl.Members[0]);
    var body = Assert.IsType<BlockStmt>(methodDecl.Body);
    var variableDecl = Assert.IsType<VariableDecl>(body.Statements[0]);
    var lambdaExpr = Assert.IsType<LambdaExpr>(variableDecl.Initializer);

    var methodRef = AstResolver.AddressOfMember(table[classDecl], "run");
    var lambdaRef = AstResolver.AddressOfLambda(methodRef, lambdaExpr.Arrow);

    Assert.Equal($"module:entry#Animal.run.<lambda>@{lambdaExpr.Arrow.Line}:{lambdaExpr.Arrow.Column}", lambdaRef.ToString());
    Assert.Same(lambdaExpr, AstResolver.Resolve(graph, lambdaRef));
  }
}
