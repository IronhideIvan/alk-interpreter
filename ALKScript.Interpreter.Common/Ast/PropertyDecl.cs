using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>
  /// A property declaration inside a class:
  ///   accessModifier? "static"? "virtual"? "abstract"? "override"? "readonly"? "property" type name
  ///   "{" ( getter? setter? ) "}"
  ///
  /// Auto-property: getter and/or setter body is null (terminated by ";").
  /// Full property: getter and/or setter has a BlockStmt body.
  /// </summary>
  public class PropertyDecl : MemberDecl
  {
    public ALKScriptToken Name { get; }
    public TypeNode Type { get; }
    public bool IsStatic { get; }
    public OverrideModifier OverrideModifier { get; }

    /// <summary>True if a get accessor is declared.</summary>
    public bool HasGetter { get; }

    /// <summary>True if a set accessor is declared.</summary>
    public bool HasSetter { get; }

    /// <summary>
    /// Body of the get accessor, or null for an auto-property getter (get;).
    /// </summary>
    public BlockStmt? GetterBody { get; }

    /// <summary>
    /// Body of the set accessor, or null for an auto-property setter (set;),
    /// or for a property with no setter at all (HasSetter is false).
    /// </summary>
    public BlockStmt? SetterBody { get; }

    /// <summary>Whether this property's accessors are implemented by the host, not by script bodies.</summary>
    public bool IsNative { get; }

    /// <summary>
    /// Whether this is an abstract property (no body on either accessor, just ";").
    /// </summary>
    public bool IsAbstract => OverrideModifier == OverrideModifier.Abstract;

    public PropertyDecl(
      AccessModifier accessModifier,
      ALKScriptToken name,
      TypeNode type,
      bool isStatic,
      OverrideModifier overrideModifier,
      bool hasGetter,
      BlockStmt? getterBody,
      bool hasSetter,
      BlockStmt? setterBody,
      bool isNative = false)
      : base(accessModifier)
    {
      Name = name;
      Type = type;
      IsStatic = isStatic;
      OverrideModifier = overrideModifier;
      HasGetter = hasGetter;
      GetterBody = getterBody;
      HasSetter = hasSetter;
      SetterBody = setterBody;
      IsNative = isNative;
    }
  }
}
