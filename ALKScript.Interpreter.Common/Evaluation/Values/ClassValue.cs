using System.Collections.Generic;
using ALKScript.Interpreter.Common.Ast;

namespace ALKScript.Interpreter.Common.Evaluation.Values
{
  /// <summary>
  /// The runtime representation of a class itself (as opposed to an instance
  /// of it): used for "new" expression construction, static member access,
  /// and method-table lookup that walks the superclass chain.
  /// </summary>
  public sealed class ClassValue : ALKScriptValue
  {
    public ClassDecl Declaration { get; }
    public ClassValue? Superclass { get; }

    public ClassValue(ClassDecl declaration, ClassValue? superclass)
    {
      Declaration = declaration;
      Superclass = superclass;
    }

    public override string TypeName => "class";

    public override string ToString() => $"<class {Declaration.Name.Lexeme}>";

    /// <summary>Finds a member by name on this class or, failing that, its superclass chain.</summary>
    public MemberDecl? FindMember(string name)
    {
      for (ClassValue? current = this; current != null; current = current.Superclass)
      {
        foreach (var member in current.Declaration.Members)
        {
          if (MemberName(member) == name)
          {
            return member;
          }
        }
      }

      return null;
    }

    private static string? MemberName(MemberDecl member)
    {
      switch (member)
      {
        case MethodDecl method:
          return method.Name.Lexeme;
        case FieldDecl field:
          return field.Name.Lexeme;
        default:
          return null;
      }
    }
  }

  /// <summary>An instantiated object: a class paired with its own field storage.</summary>
  public sealed class InstanceValue : ALKScriptValue
  {
    public ClassValue Class { get; }
    public Dictionary<string, ALKScriptValue> Fields { get; }

    public InstanceValue(ClassValue @class)
    {
      Class = @class;
      Fields = new Dictionary<string, ALKScriptValue>();
    }

    public override string TypeName => Class.Declaration.Name.Lexeme;

    public override string ToString() => $"<instance of {Class.Declaration.Name.Lexeme}>";
  }
}
