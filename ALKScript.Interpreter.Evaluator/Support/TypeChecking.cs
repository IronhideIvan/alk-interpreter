using System.Collections.Generic;
using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Common.Evaluation;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Evaluator
{
  /// <summary>
  /// Enforces type annotations at the points where a value is assigned,
  /// passed, or returned:
  ///
  ///   - A type without a trailing "?" is non-nullable: assigning "null" to
  ///     it is a <see cref="RuntimeException"/>.
  ///   - A bare class/interface type-parameter reference (e.g. "T" on
  ///     <c>class Box&lt;T&gt;</c>) is checked against the concrete type
  ///     argument the enclosing instance was constructed with (via
  ///     <see cref="ScriptEnvironment.CurrentTypeArguments"/>) — e.g. assigning
  ///     a "string" to a "T" slot on a "Box&lt;int&gt;" instance is a
  ///     <see cref="RuntimeException"/>. A generic class must always be
  ///     constructed with type arguments (<c>new Box(...)</c> with no type
  ///     arguments is itself a <see cref="RuntimeException"/> at the "new"
  ///     site), so every "T" slot has a recorded argument. Any type other
  ///     than a bare "T" (such as "T[]" or "Array&lt;T&gt;") is left
  ///     unconstrained, matching the language's existing type-erased generics.
  /// </summary>
  internal static class TypeChecking
  {
    public static void EnsureAssignable(TypeNode? type, ALKScriptValue value, ALKScriptToken site, string description, ScriptEnvironment environment, IReadOnlyDictionary<string, TypeNode>? typeArguments = null)
    {
      if (type == null)
      {
        return;
      }

      var (effectiveType, substituted) = Substitute(type, typeArguments ?? environment.CurrentTypeArguments);

      if (value is NullValue)
      {
        if (!effectiveType.IsNullable)
        {
          throw new RuntimeException(site, $"Cannot assign 'null' to {description} of non-nullable type '{type}'.");
        }

        return;
      }

      if (substituted && !MatchesType(value, effectiveType, environment, site))
      {
        throw new RuntimeException(site, $"Cannot assign a value of type '{value.TypeName}' to {description} of type '{type}' (instantiated as '{effectiveType}').");
      }
    }

    /// <summary>
    /// Replaces a bare type-parameter reference (e.g. "T") with the concrete
    /// type argument recorded for it in <see cref="ScriptEnvironment.CurrentTypeArguments"/>,
    /// if any. A trailing "?" on the original reference (e.g. "T?") carries
    /// over even if the concrete argument itself is non-nullable. Any other
    /// shape of <paramref name="type"/> (arrays, generic instantiations,
    /// primitives, named types) is returned unchanged.
    /// </summary>
    private static (TypeNode Type, bool Substituted) Substitute(TypeNode type, IReadOnlyDictionary<string, TypeNode>? typeArguments)
    {
      if (type.ArrayRank == 0
        && type.TypeArguments.Count == 0
        && typeArguments != null
        && typeArguments.TryGetValue(type.Name, out var concrete))
      {
        if (type.IsNullable && !concrete.IsNullable)
        {
          concrete = new TypeNode(concrete.Name, concrete.TypeArguments, concrete.ArrayRank, isNullable: true);
        }

        return (concrete, true);
      }

      return (type, false);
    }

    /// <summary>
    /// Implements the runtime semantics of <c>is</c>/<c>as</c>: whether
    /// <paramref name="value"/> is an instance of <paramref name="type"/>.
    /// User-defined type names (classes, interfaces, enums) are resolved
    /// against <paramref name="environment"/>.
    /// </summary>
    public static bool MatchesType(ALKScriptValue value, TypeNode type, ScriptEnvironment environment, ALKScriptToken site)
    {
      if (value is NullValue)
      {
        return type.IsNullable;
      }

      if (type.ArrayRank > 0)
      {
        return value is ArrayValue;
      }

      switch (type.Name)
      {
        case "int":
        case "long":
          return value is IntValue;
        case "float":
          return value is FloatValue;
        case "string":
          return value is StringValue;
        case "bool":
          return value is BoolValue;
        case "void":
          return false;
        default:
          return MatchesNamedType(value, type.Name, environment, site);
      }
    }

    private static bool MatchesNamedType(ALKScriptValue value, string typeName, ScriptEnvironment environment, ALKScriptToken site)
    {
      if (!environment.TryGet(typeName, out var typeValue))
      {
        throw new RuntimeException(site, $"Unknown type '{typeName}'.");
      }

      switch (typeValue)
      {
        case ClassValue classValue:
          return value is InstanceValue instance && IsInstanceOfClass(instance.Class, classValue);

        case InterfaceValue interfaceValue:
          return value is InstanceValue instanceValue && ImplementsInterface(instanceValue.Class, interfaceValue);

        case EnumTypeValue enumType:
          return value is EnumValue enumValue && enumValue.EnumName == enumType.Declaration.Name.Lexeme;

        default:
          throw new RuntimeException(site, $"'{typeName}' is not a type that can be used with 'is'/'as'.");
      }
    }

    private static bool IsInstanceOfClass(ClassValue? actual, ClassValue target)
    {
      for (var current = actual; current != null; current = current.Superclass)
      {
        if (ReferenceEquals(current.Declaration, target.Declaration))
        {
          return true;
        }
      }

      return false;
    }

    private static bool ImplementsInterface(ClassValue? cls, InterfaceValue target)
    {
      for (var current = cls; current != null; current = current.Superclass)
      {
        foreach (var implemented in current.Interfaces)
        {
          if (InterfaceMatches(implemented, target))
          {
            return true;
          }
        }
      }

      return false;
    }

    private static bool InterfaceMatches(InterfaceValue candidate, InterfaceValue target)
    {
      if (ReferenceEquals(candidate.Declaration, target.Declaration))
      {
        return true;
      }

      foreach (var extended in candidate.Extends)
      {
        if (InterfaceMatches(extended, target))
        {
          return true;
        }
      }

      return false;
    }
  }
}
