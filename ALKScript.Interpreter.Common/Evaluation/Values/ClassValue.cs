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
    public IReadOnlyList<InterfaceValue> Interfaces { get; }

    /// <summary>
    /// The environment the class was declared in, captured as a closure so its
    /// methods and constructors can see enclosing module-level bindings (other
    /// top-level declarations, imports, etc.) — the same way a function's body
    /// closes over the scope it was declared in.
    /// </summary>
    public ScriptEnvironment Closure { get; }

    public ClassValue(ClassDecl declaration, ClassValue? superclass, ScriptEnvironment closure, IReadOnlyList<InterfaceValue>? interfaces = null)
    {
      Declaration = declaration;
      Superclass = superclass;
      Closure = closure;
      Interfaces = interfaces ?? System.Array.Empty<InterfaceValue>();
    }

    public override string TypeName => "class";

    public override string ToString() => $"<class {Declaration.Name.Lexeme}>";

    /// <summary>Finds a member by name on this class or, failing that, its superclass chain.</summary>
    public MemberDecl? FindMember(string name) => FindMember(name, out _);

    /// <summary>
    /// Like <see cref="FindMember(string)"/>, but also reports which class in
    /// the chain — this one or a superclass — actually declares the member.
    /// Resolving a <c>native</c> method's host binding needs the declaring
    /// class (bindings are scoped to it; see <see cref="ScriptNativeMethodBindings"/>),
    /// not the runtime type of the receiving instance, so a binding
    /// registered against a superclass is found through any subclass that
    /// merely inherits the method.
    /// </summary>
    public MemberDecl? FindMember(string name, out ClassValue? declaringClass)
    {
      for (ClassValue? current = this; current != null; current = current.Superclass)
      {
        foreach (var member in current.Declaration.Members)
        {
          if (MemberName(member) == name)
          {
            declaringClass = current;
            return member;
          }
        }
      }

      declaringClass = null;
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
}
