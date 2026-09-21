using Silk.NET.OpenGL;

namespace Goreal.Core.Graphics;

public sealed class Shader : IDisposable
{
    readonly GL _gl;
    readonly uint _handle;
    public Shader(GL gl, string vertexSrc, string fragmentSrc)
    {
        _gl = gl;
        uint vs = Compile(GLEnum.VertexShader, vertexSrc);
        uint fs = Compile(GLEnum.FragmentShader, fragmentSrc);
        _handle = _gl.CreateProgram();
        _gl.AttachShader(_handle, vs);
        _gl.AttachShader(_handle, fs);
        _gl.LinkProgram(_handle);
        _gl.GetProgram(_handle, GLEnum.LinkStatus, out int ok);
        if (ok == 0) throw new Exception($"Shader link failed: {_gl.GetProgramInfoLog(_handle)}");
        _gl.DetachShader(_handle, vs); _gl.DeleteShader(vs);
        _gl.DetachShader(_handle, fs); _gl.DeleteShader(fs);
    }
    uint Compile(GLEnum type, string src)
    {
        uint h = _gl.CreateShader(type);
        _gl.ShaderSource(h, src);
        _gl.CompileShader(h);
        _gl.GetShader(h, GLEnum.CompileStatus, out int ok);
        if (ok == 0) throw new Exception($"{type} compile: {_gl.GetShaderInfoLog(h)}");
        return h;
    }
    public uint Handle => _handle;
    public void Use() => _gl.UseProgram(_handle);
    public void SetMat4(string name, System.Numerics.Matrix4x4 m)
    {
        int loc = _gl.GetUniformLocation(_handle, name);
        unsafe { _gl.UniformMatrix4(loc, 1, false, GetMatrix4x4Pointer(ref m)); }
    }
    public void SetVec3(string name, System.Numerics.Vector3 v)
    {
        int loc = _gl.GetUniformLocation(_handle, name);
        _gl.Uniform3(loc, v.X, v.Y, v.Z);
    }
    static unsafe float* GetMatrix4x4Pointer(ref System.Numerics.Matrix4x4 m)
    {
        fixed (System.Numerics.Matrix4x4* p = &m) return (float*)p;
    }
    public void Dispose() => _gl.DeleteProgram(_handle);
}
