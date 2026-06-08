namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>Base type for class members: constructors, fields, and methods.</summary>
  public abstract class MemberDecl
  {
    public AccessModifier AccessModifier { get; }

    protected MemberDecl(AccessModifier accessModifier)
    {
      AccessModifier = accessModifier;
    }
  }
}
