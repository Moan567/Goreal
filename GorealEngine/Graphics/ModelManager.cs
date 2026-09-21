using Silk.NET.OpenGL;

namespace Goreal.Core.Graphics;

public sealed class ModelManager : IDisposable
{
    readonly GL _gl;
    readonly TextureManager _texMan;
    readonly Dictionary<string, Model> _cache = new(StringComparer.OrdinalIgnoreCase);

    public ModelManager(GL gl, TextureManager texMan){ _gl=gl; _texMan=texMan; }

    public Model Get(string logicalPath)
    {
        // logicalPath like "models/crate.obj" relative to Game/
        if (_cache.TryGetValue(logicalPath, out var m)) return m;

        string? found = FindModel(logicalPath);
        if (found == null) throw new FileNotFoundException($"Model not found: {logicalPath} (searched Game/models, textures, etc.)");

        var model = new Model(_gl, found, _texMan);
        _cache[logicalPath]=model;
        Console.WriteLine($"[Model] loaded {logicalPath} -> {found}");
        return model;
    }

    string? FindModel(string logical)
    {
        string[] roots = {
            Path.Combine(AppContext.BaseDirectory, ""),
            Path.Combine(Directory.GetCurrentDirectory(), ""),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "Game"),
            @"C:\Users\QuipG\OneDrive\Desktop\Goreal\Game",
            @"C:\Users\QuipG\OneDrive\Desktop\Goreal",
            Path.GetDirectoryName(AppContext.BaseDirectory) ?? "",
        };
        string[] tries = { logical, Path.Combine("models", Path.GetFileName(logical)), logical.Replace("models/","") };
        foreach(var root in roots.Distinct())
        {
            if(string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
            foreach(var t in tries){
                var p = Path.Combine(root, t);
                if(File.Exists(p)) return p;
                // also try with textures prefix? no
            }
            // recursive search for filename
            try{
                var fn = Path.GetFileName(logical);
                var files = Directory.GetFiles(root, fn, SearchOption.AllDirectories);
                if(files.Length>0) return files[0];
            }catch{}
        }
        return null;
    }

    public void Dispose(){ foreach(var m in _cache.Values) m.Dispose(); _cache.Clear(); }
}
