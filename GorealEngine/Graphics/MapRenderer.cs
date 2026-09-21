using System.Numerics;
using Silk.NET.OpenGL;
using Goreal.Core.Scene;

namespace Goreal.Core.Graphics;

public sealed class MapRenderer : IDisposable
{
    readonly GL _gl;
    readonly Shader _shader;
    public Shader Shader => _shader;
    uint _vao, _vbo;
    bool _initialized;
    TextureManager? _texMan;
    readonly List<Batch> _batches = new();

    struct Batch
    {
        public Texture? Tex;
        public string Name;
        public int Start; // vertex offset
        public int Count; // vertex count
        public bool Skip;
        public bool IsDoor;
        public int DoorEntityId; // for door batches
    }

    // vertex: pos3 normal3 uv2 = 8 floats
    const string VertexSrc = @"#version 330 core
layout(location=0) in vec3 aPos;
layout(location=1) in vec3 aNormal;
layout(location=2) in vec2 aUV;
uniform mat4 uView;
uniform mat4 uProj;
uniform mat4 uModel;
out vec3 vNormal;
out vec2 vUV;
out float vDist;
void main(){
    vec4 worldPos = uModel * vec4(aPos,1.0);
    vec4 viewPos = uView * worldPos;
    gl_Position = uProj * viewPos;
    vNormal = mat3(transpose(inverse(uModel))) * aNormal;
    vUV = aUV;
    vDist = -viewPos.z;
}";

    const string FragmentSrc = @"#version 330 core
in vec3 vNormal;
in vec2 vUV;
in float vDist;
uniform sampler2D uTex;
uniform bool uUseTex;
uniform vec3 uFallback;
out vec4 FragColor;
void main(){
    vec3 lightDir = normalize(vec3(0.6, 1.0, 0.4));
    float diff = max(dot(normalize(vNormal), lightDir), 0.0);
    float ambient = 0.45;
    vec3 baseCol = uUseTex ? texture(uTex, vUV).rgb : uFallback;
    // fog in view-depth, pushed back from player
    float fog = clamp((vDist - 30.0)/50.0, 0.0, 0.65);
    vec3 lit = baseCol * (ambient + diff*0.55);
    lit = mix(lit, vec3(0.53,0.66,0.78), fog);
    FragColor = vec4(lit, 1.0);
}";

    public MapRenderer(GL gl) { _gl = gl; _shader = new Shader(gl, VertexSrc, FragmentSrc); }

    // Door info for building dynamic batches
    public struct DoorBuildInfo
    {
        public int EntityId;
        public List<MapBrush> Brushes;
    }

    public void Build(Map map)
    {
        Build(map, null, null);
    }

    public void Build(Map map, TextureManager? texMan)
    {
        Build(map, texMan, null);
    }

    public void Build(Map map, TextureManager? texMan, List<DoorBuildInfo>? doors)
    {
        _texMan = texMan;
        _batches.Clear();
        var verts = new List<float>();
        // group faces by texture to minimize binds
        var groups = new Dictionary<string, List<MapFace>>(StringComparer.OrdinalIgnoreCase);
        foreach (var brush in map.Brushes)
            foreach (var face in brush.Faces)
            {
                if (face.Texture.StartsWith("__TB", StringComparison.OrdinalIgnoreCase)) continue;
                if (!groups.TryGetValue(face.Texture, out var lst)) groups[face.Texture] = lst = new();
                lst.Add(face);
            }

        int vertexCursor = 0;
        foreach (var kv in groups)
        {
            string texName = kv.Key;
            Texture? tex = texMan?.Get(texName);
            int texW = tex?.Width ?? 64;
            int texH = tex?.Height ?? 64;

            int start = vertexCursor;
            foreach (var face in kv.Value)
            {
                if (face.Vertices.Count < 3) continue;
                ComputeUVs(face, texW, texH);
                var v0 = face.Vertices[0];
                var uv0 = face.UVs[0];
                for (int i = 1; i + 1 < face.Vertices.Count; i++)
                {
                    var v1 = face.Vertices[i]; var uv1 = face.UVs[i];
                    var v2 = face.Vertices[i+1]; var uv2 = face.UVs[i+1];
                    var n = Vector3.Normalize(Vector3.Cross(v1 - v0, v2 - v0));
                    if (Vector3.Dot(n, face.Normal) < 0)
                    {
                        AddTri(verts, v0, v2, v1, face.Normal, uv0, uv2, uv1);
                        vertexCursor += 3;
                    }
                    else
                    {
                        AddTri(verts, v0, v1, v2, face.Normal, uv0, uv1, uv2);
                        vertexCursor += 3;
                    }
                }
            }
            int count = vertexCursor - start;
            if (count > 0) _batches.Add(new Batch { Name = texName, Tex = tex, Start = start, Count = count, IsDoor = false });
        }

        // dynamic door batches (grouped by door+texture so each door can have its own model matrix)
        if (doors != null)
        {
            foreach (var d in doors)
            {
                var doorGroups = new Dictionary<string, List<MapFace>>(StringComparer.OrdinalIgnoreCase);
                foreach (var brush in d.Brushes)
                    foreach (var face in brush.Faces)
                    {
                        if (face.Texture.StartsWith("__TB", StringComparison.OrdinalIgnoreCase)) continue;
                        if (!doorGroups.TryGetValue(face.Texture, out var lst)) doorGroups[face.Texture] = lst = new();
                        lst.Add(face);
                    }
                foreach (var kv in doorGroups)
                {
                    string texName = kv.Key;
                    Texture? tex = texMan?.Get(texName);
                    int texW = tex?.Width ?? 64;
                    int texH = tex?.Height ?? 64;
                    int start = vertexCursor;
                    foreach (var face in kv.Value)
                    {
                        if (face.Vertices.Count < 3) continue;
                        ComputeUVs(face, texW, texH);
                        var v0 = face.Vertices[0]; var uv0 = face.UVs[0];
                        for (int i = 1; i + 1 < face.Vertices.Count; i++)
                        {
                            var v1 = face.Vertices[i]; var uv1 = face.UVs[i];
                            var v2 = face.Vertices[i+1]; var uv2 = face.UVs[i+1];
                            var n = Vector3.Normalize(Vector3.Cross(v1 - v0, v2 - v0));
                            if (Vector3.Dot(n, face.Normal) < 0)
                            {
                                AddTri(verts, v0, v2, v1, face.Normal, uv0, uv2, uv1);
                                vertexCursor += 3;
                            }
                            else
                            {
                                AddTri(verts, v0, v1, v2, face.Normal, uv0, uv1, uv2);
                                vertexCursor += 3;
                            }
                        }
                    }
                    int count = vertexCursor - start;
                    if (count>0) _batches.Add(new Batch { Name = texName, Tex = tex, Start = start, Count = count, IsDoor = true, DoorEntityId = d.EntityId });
                }
            }
        }

        var arr = verts.ToArray();
        if (!_initialized) { _vao = _gl.GenVertexArray(); _vbo = _gl.GenBuffer(); _initialized = true; }
        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        unsafe
        {
            fixed (float* p = arr)
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(arr.Length * sizeof(float)), p, BufferUsageARB.StaticDraw);
            // pos
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 8 * sizeof(float), (void*)0);
            _gl.EnableVertexAttribArray(1);
            _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 8 * sizeof(float), (void*)(3 * sizeof(float)));
            _gl.EnableVertexAttribArray(2);
            _gl.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, 8 * sizeof(float), (void*)(6 * sizeof(float)));
        }
        _gl.BindVertexArray(0);
        Console.WriteLine($"[MapRenderer] built {map.Brushes.Count} brushes -> {vertexCursor} verts ({vertexCursor/3} tris) in {_batches.Count} batches");
        foreach (var b in _batches) Console.WriteLine($"  batch '{b.Name}' {(b.Tex != null ? $"{b.Tex.Width}x{b.Tex.Height}" : "fallback")} verts {b.Count}");
    }

    static void ComputeUVs(MapFace face, int texW, int texH)
    {
        // Valve or Standard
        for (int i = 0; i < face.Vertices.Count; i++)
        {
            var enginePos = face.Vertices[i];
            Vector2 uv;
            if (face.Tex.IsValve)
                uv = ComputeValveUV(enginePos, face.Tex, texW, texH);
            else
                uv = ComputeStandardUV(enginePos, face, texW, texH);
            // expand list if needed
            if (i < face.UVs.Count) face.UVs[i] = uv;
            else face.UVs.Add(uv);
        }
    }

    static Vector2 ComputeValveUV(Vector3 enginePos, TexInfo ti, int texW, int texH)
    {
        // engine -> quake (undo scale+swizzle)
        Vector3 q = MapLoader.EngineToQuake(enginePos);
        float s = Vector3.Dot(q, ti.UAxis) + ti.UOff;
        float t = Vector3.Dot(q, ti.VAxis) + ti.VOff;
        // Valve axes are in quake world units; texture size + scale gives texel density
        float u = s / (texW * ti.ScaleU);
        float v = t / (texH * ti.ScaleV);
        // OpenGL V is flipped vs Quake? qmap uses V inverted; we keep as-is but negate V to match typical
        return new Vector2(u, v);
    }

    static Vector2 ComputeStandardUV(Vector3 enginePos, MapFace face, int texW, int texH)
    {
        Vector3 q = MapLoader.EngineToQuake(enginePos);
        // build basis on face plane using quake normal (convert engine normal to quake)
        // engine normal (x,y,z) -> quake normal (x, -z, y)
        Vector3 qNormal = new Vector3(face.Normal.X, -face.Normal.Z, face.Normal.Y);
        qNormal = Vector3.Normalize(qNormal);
        // choose projection plane similar to Quake's "TextureAxisFromPlane"
        Vector3 sAxis, tAxis;
        // Use world-aligned axes rotated by ti.Rotation
        float absX = MathF.Abs(qNormal.X), absY = MathF.Abs(qNormal.Y), absZ = MathF.Abs(qNormal.Z);
        if (absZ >= absX && absZ >= absY) // floor/ceil
        {
            sAxis = new Vector3(1, 0, 0);
            tAxis = new Vector3(0, -1, 0);
        }
        else if (absX >= absY)
        {
            sAxis = new Vector3(0, 1, 0);
            tAxis = new Vector3(0, 0, -1);
        }
        else
        {
            sAxis = new Vector3(1, 0, 0);
            tAxis = new Vector3(0, 0, -1);
        }

        // rotate around normal
        if (face.Tex.Rotation != 0)
        {
            float rad = face.Tex.Rotation * MathF.PI / 180f;
            float cos = MathF.Cos(rad), sin = MathF.Sin(rad);
            // rotate sAxis/tAxis in plane
            // project onto plane then rotate?
            // Simplify: rotate 2D s/t around normal using generic rotation (approx)
            // We'll use quaternion rotate
            var qrot = Quaternion.CreateFromAxisAngle(qNormal, rad);
            sAxis = Vector3.Transform(sAxis, qrot);
            tAxis = Vector3.Transform(tAxis, qrot);
        }

        float s = Vector3.Dot(q, sAxis) + face.Tex.OffU;
        float t = Vector3.Dot(q, tAxis) + face.Tex.OffV;
        float u = s / (texW * face.Tex.ScaleU);
        float v = t / (texH * face.Tex.ScaleV);
        return new Vector2(u, v);
    }

    static void AddTri(List<float> dst, Vector3 a, Vector3 b, Vector3 c, Vector3 n, Vector2 ua, Vector2 ub, Vector2 uc)
    {
        dst.AddRange(new[] { a.X, a.Y, a.Z, n.X, n.Y, n.Z, ua.X, ua.Y });
        dst.AddRange(new[] { b.X, b.Y, b.Z, n.X, n.Y, n.Z, ub.X, ub.Y });
        dst.AddRange(new[] { c.X, c.Y, c.Z, n.X, n.Y, n.Z, uc.X, uc.Y });
    }

    static Vector3 ColorForTexture(string tex)
    {
        int h = tex.GetHashCode();
        var r = new Random(h);
        float hue = (float)r.NextDouble();
        if (tex.Contains("floor") || tex.Contains("ground")) return new Vector3(0.55f,0.50f,0.45f);
        if (tex.Contains("wall")) return new Vector3(0.75f,0.73f,0.70f);
        if (tex.Contains("ceil")) return new Vector3(0.82f,0.82f,0.80f);
        float s = 0.25f + (float)r.NextDouble()*0.25f;
        float v = 0.65f + (float)r.NextDouble()*0.20f;
        return HsvToRgb(hue*360f,s,v);
    }
    static Vector3 HsvToRgb(float h,float s,float v)
    {
        float c=v*s; float x=c*(1-MathF.Abs((h/60f)%2-1)); float m=v-c; float r=0,g=0,b=0;
        if(h<60){r=c;g=x;} else if(h<120){r=x;g=c;} else if(h<180){g=c;b=x;} else if(h<240){g=x;b=c;} else if(h<300){r=x;b=c;} else{r=c;b=x;}
        return new(r+m,g+m,b+m);
    }

    public void Render(Camera cam, float aspect, Goreal.Core.ECS.World? world = null)
    {
        if (_batches.Count==0) return;
        _shader.Use();
        _shader.SetMat4("uView", cam.View);
        _shader.SetMat4("uProj", cam.Projection(aspect));
        int texLoc = _gl.GetUniformLocation(_shader.Handle, "uTex");
        int useLoc = _gl.GetUniformLocation(_shader.Handle, "uUseTex");
        int fallLoc = _gl.GetUniformLocation(_shader.Handle, "uFallback");
        int modelLoc = _gl.GetUniformLocation(_shader.Handle, "uModel");
        var identity = Matrix4x4.Identity;
        // ensure default model is identity for static
        _shader.SetMat4("uModel", Matrix4x4.Identity);
        _gl.BindVertexArray(_vao);
        foreach (var b in _batches)
        {
            if (b.IsDoor && world != null)
            {
                var ent = new Goreal.Core.ECS.Entity(b.DoorEntityId);
                if (world.HasDoor(ent))
                {
                    ref var door = ref world.GetDoor(ent);
                    var m = Matrix4x4.CreateTranslation(door.CurrentPos);
                    _shader.SetMat4("uModel", m);
                }
                else if (world.HasProp(ent))
                {
                    ref var prop = ref world.GetProp(ent);
                    var t = Matrix4x4.CreateTranslation(prop.CurrentPos);
                    var r = Matrix4x4.CreateFromQuaternion(prop.CurrentRot);
                    var s = Matrix4x4.CreateScale(prop.Scale);
                    // for brush movers, verts are in world space at closed pos, so translate relative to origin
                    // prop.CurrentPos already is world pos, verts are at closed world pos, so model = translate(delta)
                    // For brush movers, origin is world, verts are at world, so delta = CurrentPos - Origin
                    var delta = prop.CurrentPos - prop.Origin;
                    var m = s * r * Matrix4x4.CreateTranslation(prop.Origin + delta);
                    // But verts are already at world, so we need to translate by delta only, and rotate around origin
                    // Simplify: if brush mover, its verts are at closed world pos, so final = rotate around origin + translate
                    // We'll compute: translate(-origin) * rotate * translate(origin+delta)
                    var toOrigin = Matrix4x4.CreateTranslation(-prop.Origin);
                    var back = Matrix4x4.CreateTranslation(prop.CurrentPos);
                    var m2 = s * toOrigin * r * back;
                    // use m2 for brush movers with rotation, else use t*r*s
                    if (prop.Mover == Goreal.Core.Components.PropMoverType.Rotate)
                        _shader.SetMat4("uModel", m2);
                    else
                        _shader.SetMat4("uModel", Matrix4x4.CreateTranslation(delta));
                }
                else
                {
                    _shader.SetMat4("uModel", Matrix4x4.Identity);
                }
            }
            else
            {
                _shader.SetMat4("uModel", Matrix4x4.Identity);
            }

            if (b.Tex != null)
            {
                _gl.Uniform1(useLoc, 1);
                b.Tex.Bind(0);
                _gl.Uniform1(texLoc, 0);
            }
            else
            {
                _gl.Uniform1(useLoc, 0);
                var col = ColorForTexture(b.Name);
                _gl.Uniform3(fallLoc, col.X, col.Y, col.Z);
            }
            _gl.DrawArrays(PrimitiveType.Triangles, b.Start, (uint)b.Count);
        }
        _gl.BindVertexArray(0);
        _gl.BindTexture(TextureTarget.Texture2D, 0);
        // helper to get pointer
        static unsafe float* GetMatrix4x4Pointer(ref Matrix4x4 m) { fixed (Matrix4x4* p = &m) return (float*)p; }
    }

    public void Dispose()
    {
        if (_initialized) { _gl.DeleteVertexArray(_vao); _gl.DeleteBuffer(_vbo); }
        _shader.Dispose();
        _texMan?.Dispose();
    }
}
