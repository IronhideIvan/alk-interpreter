using ALKScript.Interpreter.Common.Ast;
using Xunit;

namespace Tests.ALKScript.Interpreter.Parser;

public class EnumInterfaceSealedTests : ParserTestBase
{
  [Fact]
  public void Parse_EnumDeclaration_CapturesMembersAndExplicitValues()
  {
    var program = Parse("enum Color {\n  Red,\n  Green,\n  Blue = 10,\n  Cyan\n}");

    var enumDecl = Assert.IsType<EnumDecl>(Assert.Single(program.Declarations));

    Assert.Equal("Color", enumDecl.Name.Lexeme);
    Assert.Equal(4, enumDecl.Members.Count);

    Assert.Equal("Red", enumDecl.Members[0].Name.Lexeme);
    Assert.Null(enumDecl.Members[0].ExplicitValue);

    Assert.Equal("Green", enumDecl.Members[1].Name.Lexeme);
    Assert.Null(enumDecl.Members[1].ExplicitValue);

    Assert.Equal("Blue", enumDecl.Members[2].Name.Lexeme);
    Assert.Equal(10, enumDecl.Members[2].ExplicitValue);

    Assert.Equal("Cyan", enumDecl.Members[3].Name.Lexeme);
    Assert.Null(enumDecl.Members[3].ExplicitValue);
  }

  [Fact]
  public void Parse_ExportedEnumDeclaration_WrapsInExportDecl()
  {
    var program = Parse("export enum Direction {\n  North,\n  South\n}");

    var exportDecl = Assert.IsType<ExportDecl>(Assert.Single(program.Declarations));
    var enumDecl = Assert.IsType<EnumDecl>(exportDecl.Declaration);

    Assert.Equal("Direction", enumDecl.Name.Lexeme);
    Assert.Equal(2, enumDecl.Members.Count);
  }

  [Fact]
  public void Parse_InterfaceDeclaration_CapturesMethodSignatures()
  {
    var program = Parse("interface IShape {\n  float area();\n  string describe(int precision);\n}");

    var interfaceDecl = Assert.IsType<InterfaceDecl>(Assert.Single(program.Declarations));

    Assert.Equal("IShape", interfaceDecl.Name.Lexeme);
    Assert.Empty(interfaceDecl.Extends);
    Assert.Equal(2, interfaceDecl.Methods.Count);

    Assert.Equal("area", interfaceDecl.Methods[0].Name.Lexeme);
    Assert.Equal("float", interfaceDecl.Methods[0].ReturnType.Name);
    Assert.Empty(interfaceDecl.Methods[0].Parameters);

    Assert.Equal("describe", interfaceDecl.Methods[1].Name.Lexeme);
    Assert.Equal("string", interfaceDecl.Methods[1].ReturnType.Name);
    Assert.Single(interfaceDecl.Methods[1].Parameters);
    Assert.Equal("precision", interfaceDecl.Methods[1].Parameters[0].Name);
  }

  [Fact]
  public void Parse_InterfaceExtendingAnotherInterface_CapturesExtendsList()
  {
    var program = Parse("interface IMovable extends IShape, INameable {\n  void move();\n}");

    var interfaceDecl = Assert.IsType<InterfaceDecl>(Assert.Single(program.Declarations));

    Assert.Equal(new[] { "IShape", "INameable" }, new[] { interfaceDecl.Extends[0].Lexeme, interfaceDecl.Extends[1].Lexeme });
    Assert.Single(interfaceDecl.Methods);
  }

  [Fact]
  public void Parse_ClassImplementingInterfaces_CapturesInterfaceList()
  {
    var program = Parse("class Circle implements IShape, INameable {\n}");

    var classDecl = Assert.IsType<ClassDecl>(Assert.Single(program.Declarations));

    Assert.Equal(new[] { "IShape", "INameable" }, new[] { classDecl.Interfaces[0].Lexeme, classDecl.Interfaces[1].Lexeme });
  }

  [Fact]
  public void Parse_ClassExtendingAndImplementing_CapturesBothClauses()
  {
    var program = Parse("class Circle extends Shape implements IShape {\n}");

    var classDecl = Assert.IsType<ClassDecl>(Assert.Single(program.Declarations));

    Assert.Equal("Shape", classDecl.SuperclassName!.Lexeme);
    Assert.Equal("IShape", Assert.Single(classDecl.Interfaces).Lexeme);
  }

  [Fact]
  public void Parse_SealedClassDeclaration_SetsIsSealed()
  {
    var program = Parse("sealed class FinalBase {\n}");

    var classDecl = Assert.IsType<ClassDecl>(Assert.Single(program.Declarations));

    Assert.True(classDecl.IsSealed);
    Assert.False(classDecl.IsAbstract);
  }
}
