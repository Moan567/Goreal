using System.Numerics;
using Goreal.Core;
using Goreal.Core.Scenes;

namespace Goreal.Core.Physics;

public struct CollisionBrushData
{
    public float[] Planes; // packed x,y,z,w per plane
    public Vector3 Min;
    public Vector3 Max;
}

public struct TraceResult
{
    public float Fraction;     // 0..1 how far the box travelled before hitting something
    public Vector3 Normal;     // surface normal at the hit
    public Vector3 EndPos;
    public bool StartSolid;
    public bool AllSolid;
}

/// <summary>
/// Quake-style collision: convex brushes made of planes, swept-AABB traces.
/// Also use it for hitscan weapons (trace with halfExtents = Vector3.Zero).
/// </summary>
public sealed class CollisionWorld
{
    struct Brush
    {
        public Vector4[] Planes;
        public Vector3 Min, Max;
    }

    public const float Epsilon = 0.125f * Units.QuakeToMeters;

    readonly List<Brush> _brushes = new();
    readonly List<DynamicGroup> _dynamics = new();

    struct DynamicGroup
    {
        public Brush[] Brushes;
        public Vector3 Offset;
    }

    public int BrushCount => _brushes.Count;

    public void Clear() { _brushes.Clear(); _dynamics.Clear(); }
    public void ClearDynamics() => _dynamics.Clear();

    public void Load(IEnumerable<CollisionBrushData> data)
    {
        Clear();
        foreach (var d in data)
        {
            int n = d.Planes.Length / 4;
            var planes = new Vector4[n];
            for (int i = 0; i < n; i++)
                planes[i] = new Vector4(d.Planes[i * 4], d.Planes[i * 4 + 1], d.Planes[i * 4 + 2], d.Planes[i * 4 + 3]);
            _brushes.Add(new Brush { Planes = planes, Min = d.Min, Max = d.Max });
        }
    }

    public int RegisterDynamic(IEnumerable<CollisionBrushData> data)
    {
        var list = new List<Brush>();
        foreach (var d in data)
        {
            int n = d.Planes.Length / 4;
            var planes = new Vector4[n];
            for (int i = 0; i < n; i++)
                planes[i] = new Vector4(d.Planes[i * 4], d.Planes[i * 4 + 1], d.Planes[i * 4 + 2], d.Planes[i * 4 + 3]);
            list.Add(new Brush { Planes = planes, Min = d.Min, Max = d.Max });
        }
        var g = new DynamicGroup { Brushes = list.ToArray(), Offset = Vector3.Zero };
        _dynamics.Add(g);
        return _dynamics.Count - 1;
    }

    public void SetDynamicOffset(int handle, Vector3 offset)
    {
        if (handle < 0 || handle >= _dynamics.Count) return;
        var g = _dynamics[handle];
        g.Offset = offset;
        _dynamics[handle] = g;
    }

    public TraceResult Trace(Vector3 start, Vector3 end, Vector3 halfExtents)
    {
        var result = new TraceResult { Fraction = 1f, EndPos = end };

        var sweepMin = Vector3.Min(start, end) - halfExtents - new Vector3(Epsilon);
        var sweepMax = Vector3.Max(start, end) + halfExtents + new Vector3(Epsilon);

        foreach (var brush in _brushes)
        {
            if (brush.Max.X < sweepMin.X || brush.Min.X > sweepMax.X ||
                brush.Max.Y < sweepMin.Y || brush.Min.Y > sweepMax.Y ||
                brush.Max.Z < sweepMin.Z || brush.Min.Z > sweepMax.Z)
                continue;

            TraceBrush(brush, start, end, halfExtents, ref result);
            if (result.AllSolid) break;
        }

        // dynamic doors
        for (int gi = 0; gi < _dynamics.Count; gi++)
        {
            var g = _dynamics[gi];
            foreach (var baseBrush in g.Brushes)
            {
                Vector3 o = g.Offset;
                var min = baseBrush.Min + o;
                var max = baseBrush.Max + o;
                if (max.X < sweepMin.X || min.X > sweepMax.X ||
                    max.Y < sweepMin.Y || min.Y > sweepMax.Y ||
                    max.Z < sweepMin.Z || min.Z > sweepMax.Z)
                    continue;
                // offset planes
                var planes = new Vector4[baseBrush.Planes.Length];
                for (int i = 0; i < planes.Length; i++)
                {
                    var pl = baseBrush.Planes[i];
                    var n = new Vector3(pl.X, pl.Y, pl.Z);
                    float w = pl.W + Vector3.Dot(n, o);
                    planes[i] = new Vector4(n.X, n.Y, n.Z, w);
                }
                var moved = new Brush { Planes = planes, Min = min, Max = max };
                TraceBrush(moved, start, end, halfExtents, ref result);
                if (result.AllSolid) break;
            }
            if (result.AllSolid) break;
        }

        result.EndPos = start + (end - start) * result.Fraction;
        return result;
    }

    public bool OverlapsAABB(Vector3 min, Vector3 max)
    {
        foreach (var b in _brushes)
            if (!(b.Max.X < min.X || b.Min.X > max.X || b.Max.Y < min.Y || b.Min.Y > max.Y || b.Max.Z < min.Z || b.Min.Z > max.Z))
                return true;
        foreach (var g in _dynamics)
            foreach (var bb in g.Brushes)
            {
                var mn = bb.Min + g.Offset;
                var mx = bb.Max + g.Offset;
                if (!(mx.X < min.X || mn.X > max.X || mx.Y < min.Y || mn.Y > max.Y || mx.Z < min.Z || mn.Z > max.Z))
                    return true;
            }
        return false;
    }

    static void TraceBrush(in Brush brush, Vector3 start, Vector3 end, Vector3 half, ref TraceResult result)
    {
        float enter = -1f, leave = 1f;
        bool startOut = false, endOut = false;
        Vector3 hitNormal = Vector3.Zero;

        foreach (var pl in brush.Planes)
        {
            var n = new Vector3(pl.X, pl.Y, pl.Z);

            // Push the plane out by the box's extent along the normal (Minkowski sum).
            float offset = MathF.Abs(n.X) * half.X + MathF.Abs(n.Y) * half.Y + MathF.Abs(n.Z) * half.Z;
            float dist = pl.W + offset;

            float d1 = Vector3.Dot(n, start) - dist;
            float d2 = Vector3.Dot(n, end) - dist;

            if (d1 > 0) startOut = true;
            if (d2 > 0) endOut = true;

            if (d1 > 0 && (d2 >= Epsilon || d2 >= d1)) return;   // completely in front of this plane
            if (d1 <= 0 && d2 <= 0) continue;                    // completely behind it

            if (d1 > d2)
            {
                float f = MathF.Max(0f, (d1 - Epsilon) / (d1 - d2));
                if (f > enter) { enter = f; hitNormal = n; }
            }
            else
            {
                float f = MathF.Min(1f, (d1 + Epsilon) / (d1 - d2));
                if (f < leave) leave = f;
            }
        }

        if (!startOut)
        {
            result.StartSolid = true;
            if (!endOut)
            {
                result.AllSolid = true;
                result.Fraction = 0f;
            }
            return;
        }

        if (enter < leave && enter > -1f && enter < result.Fraction)
        {
            result.Fraction = MathF.Max(0f, enter);
            result.Normal = hitNormal;
        }
    }
}