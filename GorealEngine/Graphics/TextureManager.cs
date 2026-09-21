using Silk.NET.OpenGL;

namespace Goreal.Core.Graphics;

public sealed class TextureManager : IDisposable
{
    readonly GL _gl;
    readonly string _mapDir;
    readonly Dictionary<string, Texture> _cache = new(StringComparer.OrdinalIgnoreCase);

    public TextureManager(GL gl, string mapDirectory)
    {
        _gl = gl;
        _mapDir = mapDirectory ?? "";
    }

    public Texture? Get(string logicalName)
    {
        if (string.IsNullOrWhiteSpace(logicalName) || logicalName.StartsWith("__TB", StringComparison.OrdinalIgnoreCase))
            return null; // TrenchBroom internal – invisible

        // normalize: TrenchBroom stores like "textures/brick/brick1" or "brick/brick1"
        var key = logicalName.Replace('\\','/').Trim();
        // strip leading "textures/" for search – we will try both
        if (_cache.TryGetValue(key, out var cached)) return cached;

        string? found = FindFile(key);
        Texture tex;
        if (found != null)
        {
            try { tex = Texture.LoadFromFile(_gl, found, key); Console.WriteLine($"[Texture] loaded {key} -> {found} ({tex.Width}x{tex.Height})"); }
            catch (Exception ex) { Console.WriteLine($"[Texture] failed to load {found}: {ex.Message}"); tex = Texture.CreateChecker(_gl, key); }
        }
        else
        {
            Console.WriteLine($"[Texture] not found: {key} (using checker)");
            tex = Texture.CreateChecker(_gl, key);
        }
        _cache[key] = tex;
        return tex;
    }

    string? FindFile(string logical)
    {
        // candidates
        var tries = new List<string>();
        string stripped = logical.StartsWith("textures/", StringComparison.OrdinalIgnoreCase) ? logical.Substring(9) : logical;
        string withTexPrefix = "textures/" + stripped;
        string[] exts = { ".png", ".jpg", ".jpeg", ".tga", ".bmp" };

        // search relative to map, then project roots
        string[] roots =
        {
            _mapDir,
            Path.Combine(_mapDir, ".."),
            Path.Combine(AppContext.BaseDirectory, ""),
            Path.Combine(AppContext.BaseDirectory, "textures"),
            Path.Combine(Directory.GetCurrentDirectory(), ""),
            Path.Combine(Directory.GetCurrentDirectory(), "textures"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "Game"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "Game", "textures"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Game", "textures"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "textures"),
            @"C:\Users\QuipG\OneDrive\Desktop\Goreal\Game\textures",
            @"C:\Users\QuipG\OneDrive\Desktop\Goreal\textures",
        };

        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            foreach (var cand in new[] { logical, withTexPrefix, stripped })
                foreach (var ext in exts)
                {
                    var p = Path.Combine(root, cand + ext);
                    if (File.Exists(p)) return p;
                    // also try cand already has ext? skip
                }
            // also try as-is (logical already includes ext)
            var asIs = Path.Combine(root, logical);
            if (File.Exists(asIs)) return asIs;
            var asIs2 = Path.Combine(root, withTexPrefix);
            if (File.Exists(asIs2)) return asIs2;
        }

        // absolute fallback: search all textures folders recursively for filename match
        try
        {
            var fileName = Path.GetFileName(logical);
            foreach (var root in roots.Where(Directory.Exists))
            {
                var files = Directory.GetFiles(root, fileName + ".*", SearchOption.AllDirectories);
                if (files.Length > 0) return files[0];
            }
        } catch {}

        return null;
    }

    public void Dispose()
    {
        foreach (var t in _cache.Values) t.Dispose();
        _cache.Clear();
    }
}
