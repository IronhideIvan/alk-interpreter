using ALKScript.Interpreter.Common.Modules;
using ALKScript.Interpreter.Evaluator.Cursor;

namespace ALK.Interpreter.Sample;

/// <summary>
/// The host-side representation of an enemy or interactable. Enemies carry a
/// persistent <see cref="Run"/> that is started once (to its first
/// <c>await wait()</c>) and advanced one turn per <see cref="ProgramRun.Pump"/>
/// call; interactables are re-run from <see cref="CompiledBehavior"/> each
/// time the player steps onto their tile.
/// </summary>
public sealed class Entity
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required char Glyph { get; init; }
    public required string ScriptPath { get; init; }

    public int X { get; set; }
    public int Y { get; set; }
    public int Hp { get; set; }
    public int MaxHp { get; set; }
    public int Attack { get; set; }

    public bool Alive { get; set; } = true;
    public bool Consumed { get; set; }

    public ModuleGraph? CompiledBehavior { get; set; }
    public ProgramRun? Run { get; set; }
}
