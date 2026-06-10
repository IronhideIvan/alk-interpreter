using System.Collections.Generic;
using ALKScript.Interpreter.Common.Ast;

namespace ALKScript.Interpreter.Common.Evaluation.Values
{
  /// <summary>
  /// The runtime representation of an enum type itself (as opposed to one of
  /// its members): supports member access (e.g. <c>Color.Red</c>) via
  /// <see cref="Members"/>.
  /// </summary>
  public sealed class EnumTypeValue : ALKScriptValue
  {
    public EnumDecl Declaration { get; }
    public IReadOnlyDictionary<string, EnumValue> Members { get; }

    public EnumTypeValue(EnumDecl declaration, IReadOnlyDictionary<string, EnumValue> members)
    {
      Declaration = declaration;
      Members = members;
    }

    public override string TypeName => "enum";

    public override string ToString() => $"<enum {Declaration.Name.Lexeme}>";
  }
}
