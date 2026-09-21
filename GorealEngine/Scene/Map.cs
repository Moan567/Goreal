using System.Numerics;
using Goreal.Core.Physics;

namespace Goreal.Core.Scene;

public sealed class Map
{
    public List<MapBrush> Brushes { get; } = new();
    public List<MapEntity> Entities { get; } = new();
    public Vector3 PlayerStart { get; set; }
    public float PlayerYaw { get; set; }
    public string SourceDirectory { get; set; } = "";
}

public sealed class MapEntity
{
    public string ClassName { get; set; } = "";
    public Dictionary<string, string> Properties { get; } = new();
    public List<MapBrush> Brushes { get; } = new();
}

public sealed class MapBrush
{
    public List<MapPlane> Planes { get; } = new();
    // For rendering / collision – packed planes and bounds computed after load
    public CollisionBrushData CollisionData;
    public List<MapFace> Faces { get; } = new(); // polygons for rendering
}

public struct TexInfo
{
    public bool IsValve;            // true = Valve 220 [u v] axes present
    public Vector3 UAxis;           // Valve: world axis for U
    public float UOff;
    public Vector3 VAxis;
    public float VOff;
    public float Rotation;          // degrees (Standard format)
    public float ScaleU, ScaleV;
    public float OffU, OffV;        // Standard offsets
}

public struct MapPlane
{
    public Vector3 Normal; // unit, outward
    public float Dist;     // W = dot(N, point)
    public Vector3 P1, P2, P3; // engine-space points (metres) – already converted
    public Vector3 RawP1, RawP2, RawP3; // quake-space points (for UV calc if needed)
    public string Texture;
    public TexInfo Tex;
}

public sealed class MapFace
{
    public Vector3 Normal;
    public List<Vector3> Vertices { get; } = new(); // engine metres, ccw from outside
    public List<Vector2> UVs { get; } = new(); // per-vertex UV (0..1)
    public string Texture = "";
    public TexInfo Tex;
}
