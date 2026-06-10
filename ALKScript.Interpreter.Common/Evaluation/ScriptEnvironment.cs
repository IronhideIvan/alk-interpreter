using System.Collections.Generic;
using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Common.Evaluation.Values;

namespace ALKScript.Interpreter.Common.Evaluation
{
  /// <summary>
  /// A lexical scope mapping names to runtime values, optionally chained to
  /// an enclosing scope (e.g. a module's global scope enclosing a function's
  /// local scope, which a <see cref="Values.FunctionValue"/> captures as its closure).
  /// </summary>
  public class ScriptEnvironment
  {
    private readonly ScriptEnvironment? _enclosing;
    private readonly Dictionary<string, ALKScriptValue> _values = new Dictionary<string, ALKScriptValue>();
    private readonly Dictionary<string, TypeNode?> _types = new Dictionary<string, TypeNode?>();
    private ClassValue? _currentClass;
    private TypeNode? _currentFunctionReturnType;

    public ScriptEnvironment(ScriptEnvironment? enclosing = null)
    {
      _enclosing = enclosing;
    }

    /// <summary>
    /// The class whose method (or constructor) is currently executing, used by
    /// access-modifier enforcement. Set on the innermost <c>callEnvironment</c>
    /// when invoking a bound method or constructor; walked up the scope chain
    /// so that blocks and closures nested inside a method naturally inherit it.
    /// </summary>
    public ClassValue? CurrentClass
    {
      get
      {
        for (ScriptEnvironment? scope = this; scope != null; scope = scope._enclosing)
        {
          if (scope._currentClass != null) return scope._currentClass;
        }
        return null;
      }
      set => _currentClass = value;
    }

    /// <summary>
    /// The return type of the function/method whose body is currently
    /// executing, used by <c>return</c>-statement nullability checks. Set on
    /// the innermost <c>callEnvironment</c> when invoking a function; walked
    /// up the scope chain like <see cref="CurrentClass"/> so nested blocks and
    /// closures resolve to their own enclosing function, not an outer one.
    /// </summary>
    public TypeNode? CurrentFunctionReturnType
    {
      get
      {
        for (ScriptEnvironment? scope = this; scope != null; scope = scope._enclosing)
        {
          if (scope._currentFunctionReturnType != null) return scope._currentFunctionReturnType;
        }
        return null;
      }
      set => _currentFunctionReturnType = value;
    }

    /// <summary>
    /// Binds <paramref name="name"/> to <paramref name="value"/> in this scope,
    /// shadowing any enclosing binding. <paramref name="declaredType"/> records
    /// the variable's/parameter's/field's annotated type (null for "var" or
    /// other untyped bindings), used by nullability checks on later assignment.
    /// </summary>
    public void Define(string name, ALKScriptValue value, TypeNode? declaredType = null)
    {
      _values[name] = value;
      _types[name] = declaredType;
    }

    /// <summary>
    /// Looks up the declared type bound to <paramref name="name"/> via
    /// <see cref="Define"/>, in this scope or, failing that, any enclosing
    /// scope. Returns null (with <paramref name="type"/> set to null) when the
    /// name is undefined or was declared without an explicit type.
    /// </summary>
    public bool TryGetDeclaredType(string name, out TypeNode? type)
    {
      for (ScriptEnvironment? scope = this; scope != null; scope = scope._enclosing)
      {
        if (scope._types.TryGetValue(name, out type))
        {
          return true;
        }
      }

      type = null;
      return false;
    }

    /// <summary>
    /// The bindings defined directly in this scope, excluding any enclosing scope —
    /// e.g. a module's own top-level declarations and imports.
    /// </summary>
    public IReadOnlyDictionary<string, ALKScriptValue> OwnBindings => _values;

    /// <summary>Looks up <paramref name="name"/> in this scope or, failing that, any enclosing scope.</summary>
    public bool TryGet(string name, out ALKScriptValue value)
    {
      for (ScriptEnvironment? scope = this; scope != null; scope = scope._enclosing)
      {
        if (scope._values.TryGetValue(name, out value!))
        {
          return true;
        }
      }

      value = NullValue.Instance;
      return false;
    }

    /// <summary>
    /// Assigns to an existing binding of <paramref name="name"/> in this scope or
    /// an enclosing one. Returns false when no such binding exists.
    /// </summary>
    public bool TryAssign(string name, ALKScriptValue value)
    {
      for (ScriptEnvironment? scope = this; scope != null; scope = scope._enclosing)
      {
        if (scope._values.ContainsKey(name))
        {
          scope._values[name] = value;
          return true;
        }
      }

      return false;
    }
  }
}
