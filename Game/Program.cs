using Goreal.Core;

string ResolveMap(string preferred)
{
    if (File.Exists(preferred)) return preferred;
    var candidates = new[]
    {
        Path.Combine(AppContext.BaseDirectory, preferred),
        Path.Combine(AppContext.BaseDirectory, "Maps", Path.GetFileName(preferred)),
        Path.Combine(Directory.GetCurrentDirectory(), preferred),
        Path.Combine(Directory.GetCurrentDirectory(), "Maps", Path.GetFileName(preferred)),
        Path.Combine(Directory.GetCurrentDirectory(), "..", "Game", "Maps", Path.GetFileName(preferred)),
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Game", "Maps", Path.GetFileName(preferred)),
    };
    foreach (var c in candidates) if (File.Exists(c)) return Path.GetFullPath(c);
    // fallback: first .map in any Maps folder
    foreach (var dir in new[] { Path.Combine(AppContext.BaseDirectory, "Maps"), Path.Combine(Directory.GetCurrentDirectory(), "Maps"), Path.Combine(Directory.GetCurrentDirectory(), "..", "Game", "Maps"), @"C:\Users\QuipG\OneDrive\Desktop\Goreal\Game\Maps" })
    {
        if (!Directory.Exists(dir)) continue;
        var first = Directory.GetFiles(dir, "*.map").FirstOrDefault();
        if (first != null) return first;
    }
    return preferred;
}

string requested = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "Maps", "test.map");
string mapPath = ResolveMap(requested);
if (!File.Exists(mapPath))
{
    // try Map.map legacy name
    mapPath = ResolveMap(Path.Combine(AppContext.BaseDirectory, "Maps", "Map.map"));
}

Console.WriteLine($"Loading map: {mapPath}");
if (!File.Exists(mapPath))
{
    Console.WriteLine("No .map found. Creating a minimal map in memory.");
    // fallback: ensure GameApp still runs with empty world
}

using var app = new GameApp(mapPath);
app.Run();
