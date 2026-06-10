using System.Collections.Generic;
using ALKScript.Interpreter.Common.Ast;

namespace ALKScript.Interpreter.Common.Evaluation.Values
{
  /// <summary>
  /// The runtime representation of an interface declaration. Used at
  /// class-declaration time to validate that an implementing class (or one of
  /// its superclasses) provides every method declared by the interface and
  /// any interfaces it extends.
  /// </summary>
  public sealed class InterfaceValue : ALKScriptValue
  {
    public InterfaceDecl Declaration { get; }
    public IReadOnlyList<InterfaceValue> Extends { get; }

    public InterfaceValue(InterfaceDecl declaration, IReadOnlyList<InterfaceValue> extends)
    {
      Declaration = declaration;
      Extends = extends;
    }

    public override string TypeName => "interface";

    public override string ToString() => $"<interface {Declaration.Name.Lexeme}>";

    /// <summary>
    /// All method signatures required by this interface, including those
    /// inherited (transitively) from interfaces it extends.
    /// </summary>
    public IEnumerable<InterfaceMethodSignature> AllMethods()
    {
      foreach (var method in Declaration.Methods)
      {
        yield return method;
      }

      foreach (var extended in Extends)
      {
        foreach (var method in extended.AllMethods())
        {
          yield return method;
        }
      }
    }
  }
}
