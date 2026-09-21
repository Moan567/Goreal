using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using Goreal.Core.Physics;

namespace Goreal.Core.Scene;

/// <summary>
/// Minimal Quake .map parser: handles standard and Valve 220 format.
/// Stores full TexInfo for later UV generation.
/// </summary>
public static class MapLoader
{
    // captures 3 points, texture, and remainder (for Valve/standard UV)
    static readonly Regex PlaneRegex = new(
        @"\(\s*([-\d\.]+)\s+([-\d\.]+)\s+([-\d\.]+)\s*\)\s*\(\s*([-\d\.]+)\s+([-\d\.]+)\s+([-\d\.]+)\s*\)\s*\(\s*([-\d\.]+)\s+([-\d\.]+)\s+([-\d\.]+)\s*\)\s*(\S+)?\s*(.*)",
        RegexOptions.Compiled);

    static readonly Regex ValveRegex = new(
        @"\[\s*([-\d\.]+)\s+([-\d\.]+)\s+([-\d\.]+)\s+([-\d\.]+)\s*\]\s*\[\s*([-\d\.]+)\s+([-\d\.]+)\s+([-\d\.]+)\s+([-\d\.]+)\s*\]\s*([-\d\.]+)\s+([-\d\.]+)\s+([-\d\.]+)",
        RegexOptions.Compiled);

    public static Map Load(string path) => Parse(File.ReadAllText(path), Path.GetDirectoryName(path) ?? "");

    public static Map LoadFromText(string text) => Parse(text, "");

    static Map Parse(string text, string mapDir)
    {
        var map = new Map { SourceDirectory = mapDir };
        var lines = text.Split('\n');
        MapEntity? curEntity = null;
        MapBrush? curBrush = null;
        bool inBrush = false;

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("//")) continue;

            if (line == "{")
            {
                if (curEntity == null) curEntity = new MapEntity();
                else if (!inBrush) { curBrush = new MapBrush(); inBrush = true; }
                continue;
            }
            if (line == "}")
            {
                if (inBrush && curBrush != null && curEntity != null)
                {
                    if (curBrush.Planes.Count >= 4)
                    {
                        FixPlaneOrientations(curBrush);
                        BuildCollisionAndBounds(curBrush);
                        BuildFaces(curBrush);
                        curEntity.Brushes.Add(curBrush);
                    }
                    curBrush = null; inBrush = false;
                }
                else if (curEntity != null && !inBrush)
                {
                    if (curEntity.Properties.TryGetValue("classname", out var cn)) curEntity.ClassName = cn;
                    // worldspawn and func_detail/func_group are static
                    bool isWorld = curEntity.ClassName == "worldspawn" || curEntity.ClassName == "func_detail" || curEntity.ClassName == "func_group";
                    if (isWorld)
                    {
                        foreach (var b in curEntity.Brushes) map.Brushes.Add(b);
                    }
                    if (curEntity.ClassName == "info_player_start" || curEntity.ClassName == "info_player_deathmatch")
                    {
                        if (curEntity.Properties.TryGetValue("origin", out var o))
                        {
                            map.PlayerStart = QuakeToEngine(ParseVec3(o));
                            if (curEntity.Properties.TryGetValue("angle", out var a) && float.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out var ang))
                                map.PlayerYaw = ang;
                            else if (curEntity.Properties.TryGetValue("angles", out var angs))
                                map.PlayerYaw = ParseVec3(angs).Y;
                        }
                    }
                    map.Entities.Add(curEntity);
                    curEntity = null;
                }
                continue;
            }

            if (inBrush && curBrush != null)
            {
                var m = PlaneRegex.Match(line);
                if (!m.Success) continue;
                var raw1 = new Vector3(F(m.Groups[1].Value), F(m.Groups[2].Value), F(m.Groups[3].Value));
                var raw2 = new Vector3(F(m.Groups[4].Value), F(m.Groups[5].Value), F(m.Groups[6].Value));
                var raw3 = new Vector3(F(m.Groups[7].Value), F(m.Groups[8].Value), F(m.Groups[9].Value));
                var tex = m.Groups[10].Success ? m.Groups[10].Value : "default";
                var remainder = m.Groups[11].Success ? m.Groups[11].Value.Trim() : "";

                var texInfo = ParseTexInfo(remainder);

                // Quake -> Engine swizzle
                var p1 = QuakeToEngine(raw1);
                var p2 = QuakeToEngine(raw2);
                var p3 = QuakeToEngine(raw3);

                var normal = Vector3.Cross(p2 - p1, p3 - p1);
                float len = normal.Length();
                if (len < 1e-6f) continue;
                normal /= len;
                float dist = Vector3.Dot(normal, p1);

                curBrush.Planes.Add(new MapPlane
                {
                    Normal = normal, Dist = dist,
                    P1 = p1, P2 = p2, P3 = p3,
                    RawP1 = raw1, RawP2 = raw2, RawP3 = raw3,
                    Texture = tex,
                    Tex = texInfo
                });
            }
            else if (curEntity != null && !inBrush)
            {
                if (line.StartsWith("\""))
                {
                    var kv = ParseKV(line);
                    if (kv.HasValue) curEntity.Properties[kv.Value.k] = kv.Value.v;
                }
            }
        }

        if (map.PlayerStart == Vector3.Zero && map.Brushes.Count > 0)
            map.PlayerStart = new Vector3(0, 1f, 0);

        return map;
    }

    static TexInfo ParseTexInfo(string remainder)
    {
        var ti = new TexInfo { IsValve = false, ScaleU = 1f, ScaleV = 1f };
        if (string.IsNullOrWhiteSpace(remainder)) return ti;

        var vm = ValveRegex.Match(remainder);
        if (vm.Success)
        {
            ti.IsValve = true;
            ti.UAxis = new Vector3(F(vm.Groups[1].Value), F(vm.Groups[2].Value), F(vm.Groups[3].Value));
            ti.UOff = F(vm.Groups[4].Value);
            ti.VAxis = new Vector3(F(vm.Groups[5].Value), F(vm.Groups[6].Value), F(vm.Groups[7].Value));
            ti.VOff = F(vm.Groups[8].Value);
            ti.Rotation = F(vm.Groups[9].Value);
            ti.ScaleU = F(vm.Groups[10].Value);
            ti.ScaleV = F(vm.Groups[11].Value);
            if (ti.ScaleU == 0) ti.ScaleU = 1;
            if (ti.ScaleV == 0) ti.ScaleV = 1;
            return ti;
        }

        // Standard: offU offV rot scaleU scaleV
        var parts = remainder.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 1 && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var offU)) ti.OffU = offU;
        if (parts.Length >= 2 && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var offV)) ti.OffV = offV;
        if (parts.Length >= 3 && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var rot)) ti.Rotation = rot;
        if (parts.Length >= 4 && float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var su)) ti.ScaleU = su != 0 ? su : 1;
        if (parts.Length >= 5 && float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var sv)) ti.ScaleV = sv != 0 ? sv : 1;
        return ti;
    }

    static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

    static (string k, string v)? ParseKV(string line)
    {
        int q1 = line.IndexOf('"');
        int q2 = line.IndexOf('"', q1 + 1);
        int q3 = line.IndexOf('"', q2 + 1);
        int q4 = line.IndexOf('"', q3 + 1);
        if (q1 < 0 || q2 < 0 || q3 < 0 || q4 < 0) return null;
        return (line.Substring(q1 + 1, q2 - q1 - 1), line.Substring(q3 + 1, q4 - q3 - 1));
    }

    static Vector3 ParseVec3(string s)
    {
        var parts = s.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) return Vector3.Zero;
        return new Vector3(F(parts[0]), F(parts[1]), F(parts[2]));
    }

    static void FixPlaneOrientations(MapBrush brush)
    {
        var centroid = Vector3.Zero; int cnt = 0;
        foreach (var pl in brush.Planes) { centroid += pl.P1 + pl.P2 + pl.P3; cnt += 3; }
        centroid /= cnt;
        for (int i = 0; i < brush.Planes.Count; i++)
        {
            var pl = brush.Planes[i];
            float d = Vector3.Dot(pl.Normal, centroid) - pl.Dist;
            if (d > 0) { pl.Normal = -pl.Normal; pl.Dist = -pl.Dist; brush.Planes[i] = pl; }
        }
    }

    static void BuildCollisionAndBounds(MapBrush brush)
    {
        int n = brush.Planes.Count;
        var packed = new float[n * 4];
        for (int i = 0; i < n; i++) { packed[i*4+0]=brush.Planes[i].Normal.X; packed[i*4+1]=brush.Planes[i].Normal.Y; packed[i*4+2]=brush.Planes[i].Normal.Z; packed[i*4+3]=brush.Planes[i].Dist; }
        var verts = IntersectBrush(brush);
        Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        foreach (var v in verts) { min = Vector3.Min(min, v); max = Vector3.Max(max, v); }
        if (verts.Count == 0) { min = new(-8); max = new(8); }
        else { min -= new Vector3(0.1f); max += new Vector3(0.1f); }
        brush.CollisionData = new CollisionBrushData { Planes = packed, Min = min, Max = max };
    }

    static List<Vector3> IntersectBrush(MapBrush brush)
    {
        var points = new List<Vector3>();
        int n = brush.Planes.Count;
        for (int i = 0; i < n; i++) for (int j = i+1; j < n; j++) for (int k = j+1; k < n; k++)
            if (TryIntersect(brush.Planes[i], brush.Planes[j], brush.Planes[k], out var p))
            {
                bool inside=true; foreach(var pl in brush.Planes) if (Vector3.Dot(pl.Normal,p)-pl.Dist > 0.1f){inside=false;break;}
                if (inside) points.Add(p);
            }
        return points;
    }

    static bool TryIntersect(MapPlane a, MapPlane b, MapPlane c, out Vector3 p)
    {
        float det = Vector3.Dot(a.Normal, Vector3.Cross(b.Normal, c.Normal));
        if (MathF.Abs(det) < 1e-6f) { p = default; return false; }
        var crossBC = Vector3.Cross(b.Normal, c.Normal);
        var crossCA = Vector3.Cross(c.Normal, a.Normal);
        var crossAB = Vector3.Cross(a.Normal, b.Normal);
        p = (crossBC * a.Dist + crossCA * b.Dist + crossAB * c.Dist) / det;
        return true;
    }

    static void BuildFaces(MapBrush brush)
    {
        foreach (var plane in brush.Planes)
        {
            // skip __TB_ brushes for rendering? Keep them for collision but mark faces so renderer can cull
            var poly = CreateBasePolygon(plane, 4096f);
            foreach (var clip in brush.Planes)
            {
                if (clip.Normal == plane.Normal && clip.Dist == plane.Dist) continue;
                poly = ClipPolygon(poly, clip.Normal, clip.Dist);
                if (poly.Count < 3) break;
            }
            if (poly.Count >= 3)
            {
                var face = new MapFace { Normal = plane.Normal, Texture = plane.Texture, Tex = plane.Tex };
                face.Vertices.AddRange(poly);
                // UVs will be computed later in renderer when texture size is known; init to zero
                for (int i=0;i<poly.Count;i++) face.UVs.Add(Vector2.Zero);
                brush.Faces.Add(face);
            }
        }
    }

    static List<Vector3> CreateBasePolygon(MapPlane plane, float size)
    {
        Vector3 n = plane.Normal;
        Vector3 right = Vector3.Normalize(Vector3.Cross(n, MathF.Abs(n.X) < 0.9f ? Vector3.UnitX : Vector3.UnitZ));
        Vector3 forward = Vector3.Cross(n, right);
        Vector3 center = n * plane.Dist;
        var verts = new List<Vector3>(4);
        verts.Add(center + right * size + forward * size);
        verts.Add(center - right * size + forward * size);
        verts.Add(center - right * size - forward * size);
        verts.Add(center + right * size - forward * size);
        return verts;
    }

    static List<Vector3> ClipPolygon(List<Vector3> poly, Vector3 normal, float dist)
    {
        if (poly.Count == 0) return poly;
        var outPoly = new List<Vector3>();
        for (int i = 0; i < poly.Count; i++)
        {
            var cur = poly[i]; var nxt = poly[(i+1)%poly.Count];
            float dCur = Vector3.Dot(normal, cur) - dist;
            float dNxt = Vector3.Dot(normal, nxt) - dist;
            bool inCur = dCur <= 0.01f; bool inNxt = dNxt <= 0.01f;
            if (inCur && inNxt) outPoly.Add(nxt);
            else if (inCur && !inNxt) outPoly.Add(Vector3.Lerp(cur, nxt, dCur/(dCur-dNxt)));
            else if (!inCur && inNxt) { outPoly.Add(Vector3.Lerp(cur, nxt, dCur/(dCur-dNxt))); outPoly.Add(nxt); }
        }
        return outPoly;
    }

    static Vector3 QuakeToEngine(Vector3 q) => new Vector3(q.X, q.Z, -q.Y) * Goreal.Core.Units.QuakeToMeters;
    public static Vector3 EngineToQuake(Vector3 e)
    {
        // inverse of QuakeToEngine
        var qScaled = new Vector3(e.X, -e.Z, e.Y) / Goreal.Core.Units.QuakeToMeters;
        return qScaled;
    }
}
