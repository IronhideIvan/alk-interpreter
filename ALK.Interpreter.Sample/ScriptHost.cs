using System.Text.Json;
using ALKScript.Interpreter.Common.Evaluation.Scheduling;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Common.Modules;
using ALKScript.Interpreter.Evaluator.Cursor;
using ALKScript.Interpreter.Runtime;
using ALKScript.Interpreter.Serialization;

namespace ALK.Interpreter.Sample;

/// <summary>
/// Owns the <see cref="ProgramRuntime"/>, core modules, and native bindings
/// shared by every enemy and interactable script, plus save/load support via
/// <see cref="CursorStructuralStateSerializer"/>.
/// </summary>
public sealed class ScriptHost
{
    private const string CommonModule = """
        export native function void log(string message);
        export native function int randomInt(int min, int max);
        """;

    private const string EnemyModule = """
        export class Point {
            public int x;
            public int y;

            public new(int x, int y) {
                this.x = x;
                this.y = y;
            }
        }

        native function int _selfX();
        native function int _selfY();
        native function int _playerX();
        native function int _playerY();
        native function bool _tryMove(int dx, int dy);
        native function bool _attackPlayer();
        native function thunk _wait();

        export function Point self() {
            return new Point(_selfX(), _selfY());
        }

        export function Point player() {
            return new Point(_playerX(), _playerY());
        }

        export function bool tryMove(int dx, int dy) {
            return _tryMove(dx, dy);
        }

        export function bool attackPlayer() {
            return _attackPlayer();
        }

        export function void wait() {
            await _wait();
        }
        """;

    private const string InteractModule = """
        export native function int playerHp();
        export native function void healPlayer(int amount);
        export native function void damagePlayer(int amount);
        export native function void grantGold(int amount);
        export native function void removeSelf();
        """;

    private readonly GameWorld _world;
    private readonly ProgramRuntime _runtime;
    private readonly Random _random = new();

    private Entity? _currentActor;
    private Entity? _currentInteractable;

    public ScriptHost(GameWorld world)
    {
        _world = world;
        _runtime = new ProgramRuntime();
        _runtime.OperationBinder = new TurnOperationBinder();

        _runtime.CoreModules["common"] = CommonModule;
        _runtime.CoreModules["enemy"] = EnemyModule;
        _runtime.CoreModules["interact"] = InteractModule;

        _runtime.NativeBindings["common", "log"] = arguments =>
        {
            _world.Log(((StringValue)arguments[0]).Value);
            return NullValue.Instance;
        };

        _runtime.NativeBindings["common", "randomInt"] = arguments =>
        {
            var min = (int)((IntValue)arguments[0]).Value;
            var max = (int)((IntValue)arguments[1]).Value;
            return new IntValue(_random.Next(min, max + 1));
        };

        _runtime.NativeBindings["enemy", "_selfX"] = _ => new IntValue(_currentActor!.X);
        _runtime.NativeBindings["enemy", "_selfY"] = _ => new IntValue(_currentActor!.Y);
        _runtime.NativeBindings["enemy", "_playerX"] = _ => new IntValue(_world.Player.X);
        _runtime.NativeBindings["enemy", "_playerY"] = _ => new IntValue(_world.Player.Y);

        _runtime.NativeBindings["enemy", "_tryMove"] = arguments =>
        {
            var dx = (int)((IntValue)arguments[0]).Value;
            var dy = (int)((IntValue)arguments[1]).Value;
            return BoolValue.Of(_world.TryMoveEntity(_currentActor!, dx, dy));
        };

        _runtime.NativeBindings["enemy", "_attackPlayer"] = _ =>
        {
            var actor = _currentActor!;
            if (!_world.IsAdjacentToPlayer(actor)) return BoolValue.Of(false);

            _world.Player.Hp -= actor.Attack;
            _world.Log($"The {actor.Name} hits you for {actor.Attack}.");
            return BoolValue.Of(true);
        };

        _runtime.NativeBindings["interact", "playerHp"] = _ => new IntValue(_world.Player.Hp);

        _runtime.NativeBindings["interact", "healPlayer"] = arguments =>
        {
            var amount = (int)((IntValue)arguments[0]).Value;
            _world.Player.Hp = Math.Min(_world.Player.MaxHp, _world.Player.Hp + amount);
            return NullValue.Instance;
        };

        _runtime.NativeBindings["interact", "damagePlayer"] = arguments =>
        {
            var amount = (int)((IntValue)arguments[0]).Value;
            _world.Player.Hp -= amount;
            return NullValue.Instance;
        };

        _runtime.NativeBindings["interact", "grantGold"] = arguments =>
        {
            var amount = (int)((IntValue)arguments[0]).Value;
            _world.Player.Gold += amount;
            return NullValue.Instance;
        };

        _runtime.NativeBindings["interact", "removeSelf"] = _ =>
        {
            _currentInteractable!.Consumed = true;
            return NullValue.Instance;
        };
    }

    public ModuleGraph LoadBehavior(string scriptPath) => _runtime.LoadFromFile(scriptPath);

    /// <summary>Starts a persistent enemy script, running it to its first <c>await wait()</c>.</summary>
    public void StartEnemy(Entity enemy)
    {
        _currentActor = enemy;
        enemy.Run = _runtime.RunFromGraph(enemy.CompiledBehavior!);
    }

    /// <summary>Advances a living enemy's script by exactly one AI turn.</summary>
    public void TickEnemy(Entity enemy)
    {
        _currentActor = enemy;
        enemy.Run!.Pump();
    }

    /// <summary>Runs the one-shot interactable script on the player's current tile, if any.</summary>
    public void RunInteractionIfAny()
    {
        var item = _world.InteractableAt(_world.Player.X, _world.Player.Y);
        if (item == null) return;

        _currentInteractable = item;
        _runtime.RunFromGraph(item.CompiledBehavior!);
    }

    public void SaveGame(string dir)
    {
        Directory.CreateDirectory(dir);

        var save = new WorldSave
        {
            Player = new PlayerSave
            {
                X = _world.Player.X,
                Y = _world.Player.Y,
                Hp = _world.Player.Hp,
                Gold = _world.Player.Gold,
            },
            Enemies = _world.Enemies.Select(e => new EntitySave
            {
                Id = e.Id,
                X = e.X,
                Y = e.Y,
                Hp = e.Hp,
                Alive = e.Alive,
            }).ToList(),
            Interactables = _world.Interactables.Select(i => new InteractableSave
            {
                Id = i.Id,
                Consumed = i.Consumed,
            }).ToList(),
        };

        File.WriteAllText(Path.Combine(dir, "world.json"), JsonSerializer.Serialize(save));

        foreach (var enemy in _world.Enemies.Where(e => e.Alive))
        {
            var bytes = CursorStructuralStateSerializer.Capture(enemy.Run!.Evaluator);
            File.WriteAllBytes(Path.Combine(dir, $"enemy-{enemy.Id}.json"), bytes);
        }
    }

    public bool LoadGame(string dir)
    {
        var worldPath = Path.Combine(dir, "world.json");
        if (!File.Exists(worldPath)) return false;

        var save = JsonSerializer.Deserialize<WorldSave>(File.ReadAllText(worldPath))!;

        _world.Player.X = save.Player.X;
        _world.Player.Y = save.Player.Y;
        _world.Player.Hp = save.Player.Hp;
        _world.Player.Gold = save.Player.Gold;

        foreach (var entitySave in save.Enemies)
        {
            var enemy = _world.Enemies.First(e => e.Id == entitySave.Id);
            enemy.X = entitySave.X;
            enemy.Y = entitySave.Y;
            enemy.Hp = entitySave.Hp;
            enemy.Alive = entitySave.Alive;

            if (!enemy.Alive) continue;

            var graph = _runtime.LoadFromFile(enemy.ScriptPath);
            var bytes = File.ReadAllBytes(Path.Combine(dir, $"enemy-{enemy.Id}.json"));
            var evaluator = CursorStructuralStateSerializer.Restore(
                graph, bytes, out var result,
                _runtime.NativeBindings, _runtime.NativeMethodBindings, _runtime.OperationBinder);

            enemy.CompiledBehavior = graph;
            enemy.Run = ProgramRun.Restore(evaluator, result);
        }

        foreach (var interactableSave in save.Interactables)
        {
            var item = _world.Interactables.First(i => i.Id == interactableSave.Id);
            item.Consumed = interactableSave.Consumed;
        }

        return true;
    }

    /// <summary>
    /// Backs every <c>await _wait()</c> in an enemy's persistent loop: starting
    /// suspends until the next <see cref="ProgramRun.Pump"/>, which resolves it
    /// immediately — so each <c>Pump()</c> call advances exactly one AI turn.
    /// </summary>
    private sealed class TurnOperationBinder : IAsyncOperationBinder
    {
        public OperationStatus Start(PendingOperation operation) => OperationStatus.Pending.Instance;

        public OperationStatus Poll(PendingOperation operation) => new OperationStatus.Resolved(NullValue.Instance);

        public void Discard(PendingOperation operation, Action<Exception> onFault) { }

        public void OnOperationFaulted(PendingOperation operation, Exception fault) { }
    }

    private sealed class WorldSave
    {
        public PlayerSave Player { get; set; } = new();
        public List<EntitySave> Enemies { get; set; } = new();
        public List<InteractableSave> Interactables { get; set; } = new();
    }

    private sealed class PlayerSave
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Hp { get; set; }
        public int Gold { get; set; }
    }

    private sealed class EntitySave
    {
        public string Id { get; set; } = "";
        public int X { get; set; }
        public int Y { get; set; }
        public int Hp { get; set; }
        public bool Alive { get; set; }
    }

    private sealed class InteractableSave
    {
        public string Id { get; set; } = "";
        public bool Consumed { get; set; }
    }
}
