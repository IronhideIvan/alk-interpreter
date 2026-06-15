using ALK.Interpreter.Sample;

const string SaveDir = "save";

var world = GameWorld.LoadFromFile("Assets/map.txt");
var scriptHost = new ScriptHost(world);
var renderer = new Renderer();

foreach (var enemy in world.Enemies)
{
  enemy.CompiledBehavior = scriptHost.LoadBehavior(enemy.ScriptPath);
  scriptHost.StartEnemy(enemy);
}

foreach (var item in world.Interactables)
{
  item.CompiledBehavior = scriptHost.LoadBehavior(item.ScriptPath);
}

world.Log("Arrows move. Q quits, S saves, L loads latest save.");

while (true)
{
  renderer.Draw(world);

  if (world.IsGameOver(out var endMessage))
  {
    Console.WriteLine(endMessage);
    break;
  }

  var key = Console.ReadKey(true).Key;

  if (key == ConsoleKey.Q) break;

  if (key == ConsoleKey.S)
  {
    scriptHost.SaveGame(SaveDir);
    world.Log("Game saved.");
    continue;
  }

  if (key == ConsoleKey.L)
  {
    world.Log(scriptHost.LoadGame(SaveDir) ? "Game loaded." : "No save found.");
    continue;
  }

  var (dx, dy) = MapKeyToDirection(key);
  if (dx == 0 && dy == 0) continue;

  world.PlayerAct(dx, dy);
  scriptHost.RunInteractionIfAny();

  foreach (var enemy in world.Enemies.Where(e => e.Alive))
  {
    scriptHost.TickEnemy(enemy);
  }
}

static (int dx, int dy) MapKeyToDirection(ConsoleKey key) => key switch
{
  ConsoleKey.UpArrow => (0, -1),
  ConsoleKey.DownArrow => (0, 1),
  ConsoleKey.LeftArrow => (-1, 0),
  ConsoleKey.RightArrow => (1, 0),
  _ => (0, 0),
};
