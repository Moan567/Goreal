using System.Numerics;
using Silk.NET.OpenGL;

namespace Goreal.Core.Graphics;

public sealed class Model : IDisposable
{
    public string Path { get; }
    readonly GL _gl;
    readonly List<MeshPart> _parts = new();

    struct MeshPart
    {
        public uint Vao, Vbo;
        public int VertexCount;
        public Texture? Tex;
        public string TexName;
    }

    public Model(GL gl, string path, TextureManager texMan)
    {
        _gl = gl;
        Path = path;
        var meshes = ObjLoader.Load(path);
        var dir = System.IO.Path.GetDirectoryName(path) ?? "";
        foreach (var m in meshes)
        {
            // build interleaved vertices pos3 normal3 uv2
            var verts = new List<float>(m.Positions.Count * 8);
            for(int i=0;i<m.Positions.Count;i++)
            {
                var p = m.Positions[i];
                var n = i < m.Normals.Count ? m.Normals[i] : Vector3.UnitY;
                var uv = i < m.TexCoords.Count ? m.TexCoords[i] : Vector2.Zero;
                verts.Add(p.X); verts.Add(p.Y); verts.Add(p.Z);
                verts.Add(n.X); verts.Add(n.Y); verts.Add(n.Z);
                verts.Add(uv.X); verts.Add(uv.Y);
            }
            // fix normals if zero (compute per tri)
            if (m.Normals.Count==0 || m.Normals.All(v=>v.LengthSquared()<1e-6f))
            {
                for(int i=0;i<verts.Count;i+=24) // 3 verts *8
                {
                    var p0 = new Vector3(verts[i+0], verts[i+1], verts[i+2]);
                    var p1 = new Vector3(verts[i+8], verts[i+9], verts[i+10]);
                    var p2 = new Vector3(verts[i+16],verts[i+17],verts[i+18]);
                    var n = Vector3.Normalize(Vector3.Cross(p1-p0, p2-p0));
                    for(int k=0;k<3;k++){
                        verts[i+k*8+3]=n.X; verts[i+k*8+4]=n.Y; verts[i+k*8+5]=n.Z;
                    }
                }
            }

            Texture? tex = null;
            if (!string.IsNullOrEmpty(m.Texture))
            {
                // m.Texture may be like "textures/brick/brick1.png" or "brick1.png"
                var texName = m.Texture;
                // if relative to model dir, try that
                var tryPath = System.IO.Path.Combine(dir, texName);
                if (System.IO.File.Exists(tryPath))
                    tex = Texture.LoadFromFile(gl, tryPath, texName);
                else
                    tex = texMan.Get(texName) ?? texMan.Get(System.IO.Path.GetFileNameWithoutExtension(texName));
            }

            uint vao = gl.GenVertexArray();
            uint vbo = gl.GenBuffer();
            gl.BindVertexArray(vao);
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
            unsafe{
                fixed(float* p = verts.ToArray())
                    gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(verts.Count*sizeof(float)), p, BufferUsageARB.StaticDraw);
                gl.EnableVertexAttribArray(0);
                gl.VertexAttribPointer(0,3,VertexAttribPointerType.Float,false,8*sizeof(float),(void*)0);
                gl.EnableVertexAttribArray(1);
                gl.VertexAttribPointer(1,3,VertexAttribPointerType.Float,false,8*sizeof(float),(void*)(3*sizeof(float)));
                gl.EnableVertexAttribArray(2);
                gl.VertexAttribPointer(2,2,VertexAttribPointerType.Float,false,8*sizeof(float),(void*)(6*sizeof(float)));
            }
            gl.BindVertexArray(0);
            _parts.Add(new MeshPart{ Vao=vao, Vbo=vbo, VertexCount=m.Positions.Count, Tex=tex, TexName=m.Texture});
        }
        if (_parts.Count==0) throw new Exception($"No meshes in {path}");
    }

    public void Draw(Shader shader)
    {
        // shader already Use() and has uView/uProj/uModel set
        int texLoc = _gl.GetUniformLocation(shader.Handle, "uTex");
        int useLoc = _gl.GetUniformLocation(shader.Handle, "uUseTex");
        int fallLoc = _gl.GetUniformLocation(shader.Handle, "uFallback");
        foreach(var p in _parts)
        {
            _gl.BindVertexArray(p.Vao);
            if (p.Tex != null){ _gl.Uniform1(useLoc,1); p.Tex.Bind(0); _gl.Uniform1(texLoc,0); }
            else { _gl.Uniform1(useLoc,0); _gl.Uniform3(fallLoc,0.8f,0.8f,0.8f); }
            _gl.DrawArrays(PrimitiveType.Triangles,0,(uint)p.VertexCount);
        }
        _gl.BindVertexArray(0);
        _gl.BindTexture(TextureTarget.Texture2D,0);
    }

    public void Dispose()
    {
        foreach(var p in _parts){ _gl.DeleteVertexArray(p.Vao); _gl.DeleteBuffer(p.Vbo); p.Tex?.Dispose(); }
    }
}
