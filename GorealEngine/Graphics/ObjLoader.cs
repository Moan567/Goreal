using System.Globalization;
using System.Numerics;

namespace Goreal.Core.Graphics;

public sealed class ObjMesh
{
    public List<Vector3> Positions = new();
    public List<Vector2> TexCoords = new();
    public List<Vector3> Normals = new();
    // interleaved after triangulation
    public float[] Vertices = Array.Empty<float>(); // pos3 normal3 uv2
    public string Material = "";
    public string Texture = ""; // from mtl map_Kd
}

public static class ObjLoader
{
    public static List<ObjMesh> Load(string path)
    {
        var dir = Path.GetDirectoryName(path) ?? "";
        var positions = new List<Vector3>();
        var texcoords = new List<Vector2>();
        var normals = new List<Vector3>();
        var meshes = new Dictionary<string, ObjMesh>(StringComparer.OrdinalIgnoreCase);
        ObjMesh cur = new() { Material = "default" };
        meshes["default"] = cur;
        string curMtl = "default";
        var mtlMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // try load mtl first if referenced
        foreach (var line in File.ReadLines(path))
        {
            var t = line.Trim();
            if (t.StartsWith("mtllib "))
            {
                var mtlFile = t.Substring(7).Trim().Trim('"');
                var mtlPath = Path.Combine(dir, mtlFile);
                if (File.Exists(mtlPath)) ParseMtl(mtlPath, mtlMap);
            }
        }

        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length==0 || line.StartsWith("#")) continue;
            var parts = line.Split(new[]{' ','\t'}, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length==0) continue;
            switch(parts[0])
            {
                case "v":
                    positions.Add(new Vector3(F(parts[1]), F(parts[2]), F(parts[3])));
                    break;
                case "vt":
                    texcoords.Add(new Vector2(F(parts[1]), 1f - F(parts[2]))); // flip V
                    break;
                case "vn":
                    normals.Add(Vector3.Normalize(new Vector3(F(parts[1]), F(parts[2]), F(parts[3]))));
                    break;
                case "usemtl":
                    curMtl = parts.Length>1? parts[1]:"default";
                    if (!meshes.TryGetValue(curMtl, out cur!)) { cur = new(){Material=curMtl, Texture=mtlMap.TryGetValue(curMtl, out var tex)?tex:""}; meshes[curMtl]=cur; }
                    break;
                case "f":
                    // triangulate fan
                    var verts = parts.Skip(1).ToArray();
                    for(int i=1;i+1<verts.Length;i++)
                        AddTri(cur, verts[0], verts[i], verts[i+1], positions, texcoords, normals);
                    break;
            }
        }

        foreach(var m in meshes.Values)
        {
            // if no texture from mtl, keep empty
            if (string.IsNullOrEmpty(m.Texture) && mtlMap.TryGetValue(m.Material, out var tex)) m.Texture = tex;
        }
        return meshes.Values.Where(m=>m.Positions.Count>0).ToList();
    }

    static void ParseMtl(string path, Dictionary<string,string> map)
    {
        string cur = "";
        foreach(var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if(line.StartsWith("newmtl ")) cur = line.Substring(7).Trim();
            else if(line.StartsWith("map_Kd ") && !string.IsNullOrEmpty(cur))
            {
                var tex = line.Substring(7).Trim().Trim('"');
                // keep as is, may be like "textures/brick.png" or "brick.png"
                map[cur] = tex;
            }
        }
    }

    static void AddTri(ObjMesh mesh, string a, string b, string c, List<Vector3> pos, List<Vector2> uv, List<Vector3> n)
    {
        Span<string> tri = new string[]{a,b,c};
        foreach(var v in tri)
        {
            var idx = v.Split('/');
            int pi = int.Parse(idx[0]) - 1;
            int ti = idx.Length>1 && idx[1].Length>0 ? int.Parse(idx[1])-1 : -1;
            int ni = idx.Length>2 && idx[2].Length>0 ? int.Parse(idx[2])-1 : -1;
            var p = pi>=0 && pi<pos.Count ? pos[pi] : Vector3.Zero;
            var tex = ti>=0 && ti<uv.Count ? uv[ti] : Vector2.Zero;
            var norm = ni>=0 && ni<n.Count ? n[ni] : new Vector3(0,1,0);
            mesh.Positions.Add(p);
            mesh.TexCoords.Add(tex);
            mesh.Normals.Add(norm);
        }
    }

    static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);
}
