using System.Text;

namespace ALK.Interpreter.Sample;

/// <summary>Draws the maze, status line, and recent message log to the console.</summary>
public sealed class Renderer
{
    private const int MessageLines = 3;

    public void Draw(GameWorld world)
    {
        try
        {
            Console.Clear();
        }
        catch (IOException)
        {
            // No console buffer available (e.g. output redirected) — skip clearing.
        }

        var sb = new StringBuilder();

        for (var y = 0; y < world.Height; y++)
        {
            for (var x = 0; x < world.Width; x++)
            {
                sb.Append(GlyphAt(world, x, y));
            }

            sb.Append('\n');
        }

        sb.Append('\n');
        sb.Append($"HP: {world.Player.Hp}/{world.Player.MaxHp}  Gold: {world.Player.Gold}\n");
        sb.Append('\n');

        foreach (var message in world.Messages.TakeLast(MessageLines))
        {
            sb.Append(message);
            sb.Append('\n');
        }

        Console.Write(sb.ToString());
    }

    private static char GlyphAt(GameWorld world, int x, int y)
    {
        if (world.Player.X == x && world.Player.Y == y) return '@';

        var enemy = world.EnemyAt(x, y);
        if (enemy != null) return enemy.Glyph;

        var item = world.InteractableAt(x, y);
        if (item != null) return item.Glyph;

        return world.TerrainAt(x, y);
    }
}
