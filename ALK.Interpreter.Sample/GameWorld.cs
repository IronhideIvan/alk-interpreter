namespace ALK.Interpreter.Sample;

/// <summary>The host-side player state.</summary>
public sealed class Player
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Hp { get; set; } = 20;
    public int MaxHp { get; set; } = 20;
    public int Attack { get; set; } = 4;
    public int Gold { get; set; }
}

/// <summary>
/// The maze grid, player, enemies, and interactables. Owns all movement,
/// combat, and win/lose rules; <see cref="ScriptHost"/> drives behavior
/// scripts that call back into this through native bindings.
/// </summary>
public sealed class GameWorld
{
    public const char Wall = '#';
    public const char Floor = '.';
    public const char Exit = '>';

    private readonly char[][] _grid;

    public Player Player { get; } = new Player();
    public List<Entity> Enemies { get; } = new();
    public List<Entity> Interactables { get; } = new();
    public List<string> Messages { get; } = new();

    public int Width { get; }
    public int Height { get; }

    private GameWorld(char[][] grid)
    {
        _grid = grid;
        Height = grid.Length;
        Width = grid[0].Length;
    }

    public static GameWorld LoadFromFile(string path)
    {
        var lines = File.ReadAllLines(path).Where(line => line.Length > 0).ToArray();
        var grid = lines.Select(line => line.ToCharArray()).ToArray();
        var world = new GameWorld(grid);

        for (var y = 0; y < grid.Length; y++)
        {
            for (var x = 0; x < grid[y].Length; x++)
            {
                switch (grid[y][x])
                {
                    case '@':
                        world.Player.X = x;
                        world.Player.Y = y;
                        grid[y][x] = Floor;
                        break;

                    case 'g':
                        world.Enemies.Add(new Entity
                        {
                            Id = $"goblin-{world.Enemies.Count}",
                            Name = "goblin",
                            Glyph = 'g',
                            ScriptPath = "Assets/Scripts/enemies/goblin.alk",
                            X = x,
                            Y = y,
                            Hp = 12,
                            MaxHp = 12,
                            Attack = 3,
                        });
                        grid[y][x] = Floor;
                        break;

                    case 'r':
                        world.Enemies.Add(new Entity
                        {
                            Id = $"rat-{world.Enemies.Count}",
                            Name = "rat",
                            Glyph = 'r',
                            ScriptPath = "Assets/Scripts/enemies/rat.alk",
                            X = x,
                            Y = y,
                            Hp = 4,
                            MaxHp = 4,
                            Attack = 1,
                        });
                        grid[y][x] = Floor;
                        break;

                    case '!':
                        world.Interactables.Add(new Entity
                        {
                            Id = $"potion-{world.Interactables.Count}",
                            Name = "potion",
                            Glyph = '!',
                            ScriptPath = "Assets/Scripts/interactables/potion.alk",
                            X = x,
                            Y = y,
                        });
                        grid[y][x] = Floor;
                        break;

                    case '^':
                        world.Interactables.Add(new Entity
                        {
                            Id = $"trap-{world.Interactables.Count}",
                            Name = "trap",
                            Glyph = '^',
                            ScriptPath = "Assets/Scripts/interactables/trap.alk",
                            X = x,
                            Y = y,
                        });
                        grid[y][x] = Floor;
                        break;

                    case '$':
                        world.Interactables.Add(new Entity
                        {
                            Id = $"gold-{world.Interactables.Count}",
                            Name = "gold",
                            Glyph = '$',
                            ScriptPath = "Assets/Scripts/interactables/gold.alk",
                            X = x,
                            Y = y,
                        });
                        grid[y][x] = Floor;
                        break;
                }
            }
        }

        return world;
    }

    public bool IsWalkable(int x, int y)
    {
        if (x < 0 || y < 0 || y >= Height || x >= Width) return false;
        return _grid[y][x] != Wall;
    }

    public char TerrainAt(int x, int y) => _grid[y][x];

    public Entity? EnemyAt(int x, int y) =>
        Enemies.FirstOrDefault(e => e.Alive && e.X == x && e.Y == y);

    public Entity? InteractableAt(int x, int y) =>
        Interactables.FirstOrDefault(i => !i.Consumed && i.X == x && i.Y == y);

    public void Log(string message) => Messages.Add(message);

    /// <summary>
    /// Moves the player by (dx, dy): attacks an enemy occupying the target
    /// tile, or steps onto it if walkable and unoccupied.
    /// </summary>
    public void PlayerAct(int dx, int dy)
    {
        var targetX = Player.X + dx;
        var targetY = Player.Y + dy;

        var enemy = EnemyAt(targetX, targetY);
        if (enemy != null)
        {
            enemy.Hp -= Player.Attack;
            Log($"You hit the {enemy.Name} for {Player.Attack}.");
            if (enemy.Hp <= 0)
            {
                enemy.Alive = false;
                Log($"The {enemy.Name} dies.");
            }
            return;
        }

        if (!IsWalkable(targetX, targetY)) return;

        Player.X = targetX;
        Player.Y = targetY;
    }

    /// <summary>
    /// Attempts to move <paramref name="entity"/> by (dx, dy). Fails if the
    /// target tile is a wall, occupied by another enemy, or occupied by the
    /// player (enemies do not bump-attack the player on movement).
    /// </summary>
    public bool TryMoveEntity(Entity entity, int dx, int dy)
    {
        if (dx == 0 && dy == 0) return true;

        var targetX = entity.X + dx;
        var targetY = entity.Y + dy;

        if (!IsWalkable(targetX, targetY)) return false;
        if (targetX == Player.X && targetY == Player.Y) return false;
        if (EnemyAt(targetX, targetY) != null) return false;

        entity.X = targetX;
        entity.Y = targetY;
        return true;
    }

    public bool IsAdjacentToPlayer(Entity entity)
    {
        var dx = Math.Abs(entity.X - Player.X);
        var dy = Math.Abs(entity.Y - Player.Y);
        return dx <= 1 && dy <= 1 && (dx + dy) > 0;
    }

    public bool IsGameOver(out string message)
    {
        if (Player.Hp <= 0)
        {
            message = "You died. Game over.";
            return true;
        }

        if (TerrainAt(Player.X, Player.Y) == Exit)
        {
            message = "You escaped the maze! You win.";
            return true;
        }

        message = "";
        return false;
    }
}
