using System.Collections.Generic;
using ALKScript.Interpreter.Common.Ast;

namespace ALKScript.Interpreter.Evaluator.Cursor
{
  /// <summary>
  /// Identifies a pre-existing module-scope or global <see cref="ALKScript.Interpreter.Common.Evaluation.ScriptEnvironment"/>
  /// that <see cref="CursorProgramEvaluator.RestoreStructural"/>'s "run
  /// declarations, then graft" pass re-creates before grafting the captured
  /// trail/environments on top — see <see cref="CapturedEnvironment.ModuleRef"/>
  /// (docs/ASYNC_AWAIT_DESIGN.md Addendum 3).
  /// </summary>
  public sealed class ModuleEnvironmentRef
  {
    /// <summary>
    /// <c>"globals"</c> for the shared root environment global preludes run
    /// against, or <see cref="AstReference.ForModule"/>'s format
    /// (<c>"module:&lt;identifier&gt;"</c>) for a module's top-level environment.
    /// </summary>
    public string ModuleKey { get; set; } = "";

    public ModuleEnvironmentRef()
    {
    }

    public ModuleEnvironmentRef(string moduleKey)
    {
      ModuleKey = moduleKey;
    }
  }

  /// <summary>
  /// A captured <see cref="ALKScript.Interpreter.Common.Evaluation.ScriptEnvironment"/>'s
  /// "own scope" state (docs/ASYNC_AWAIT_DESIGN.md Addendum 3) — see
  /// <see cref="ALKScript.Interpreter.Common.Evaluation.ScriptEnvironment.OwnBindings"/>/
  /// <c>OwnTypes</c>/<c>OwnConsts</c>/etc (Step 5). <see cref="Id"/> indexes
  /// this entry within <see cref="CursorStructuralCaptureState.Environments"/>;
  /// <see cref="EnclosingId"/> references another entry by <see cref="Id"/>
  /// (or is <c>null</c> for a root scope).
  /// </summary>
  public sealed class CapturedEnvironment
  {
    public int Id { get; set; }

    public int? EnclosingId { get; set; }

    public Dictionary<string, CapturedHeapValue> Values { get; set; } = new();

    public Dictionary<string, TypeNode?> Types { get; set; } = new();

    public HashSet<string> Consts { get; set; } = new();

    public TypeNode? CurrentFunctionReturnType { get; set; }

    public IReadOnlyDictionary<string, TypeNode>? CurrentTypeArguments { get; set; }

    public bool IsInConstructor { get; set; }

    /// <summary>The <c>this</c> context's class, if set directly on this scope (see <c>ScriptEnvironment.OwnCurrentClass</c>).</summary>
    public CapturedHeapValue.AstRef? CurrentClass { get; set; }

    /// <summary>
    /// Set when this entry corresponds to a pre-existing module/global
    /// environment that <see cref="CursorProgramEvaluator.RestoreStructural"/>'s
    /// declarations pass re-creates, rather than a fresh scope Restore should
    /// allocate.
    /// </summary>
    public ModuleEnvironmentRef? ModuleRef { get; set; }
  }
}
