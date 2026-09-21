using Silk.NET.OpenGL;
using StbImageSharp;

namespace Goreal.Core.Graphics;

public sealed class Texture : IDisposable
{
    public uint Handle { get; }
    public int Width { get; }
    public int Height { get; }
    public string Name { get; }
    readonly GL _gl;

    public Texture(GL gl, string name, int width, int height, byte[] rgba)
    {
        _gl = gl; Name = name; Width = width; Height = height;
        Handle = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, Handle);
        unsafe
        {
            fixed (byte* p = rgba)
                gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba, (uint)width, (uint)height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, p);
        }
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.LinearMipmapLinear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.Repeat);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.Repeat);
        gl.GenerateMipmap(TextureTarget.Texture2D);
        gl.BindTexture(TextureTarget.Texture2D, 0);
    }

    public static Texture LoadFromFile(GL gl, string path, string logicalName)
    {
        using var stream = File.OpenRead(path);
        var img = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
        return new Texture(gl, logicalName, img.Width, img.Height, img.Data);
    }

    public static Texture CreateChecker(GL gl, string name, int size = 64)
    {
        var data = new byte[size * size * 4];
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            bool c = ((x / 8) + (y / 8)) % 2 == 0;
            byte v = c ? (byte)180 : (byte)90;
            // tint by name hash to distinguish
            int h = name.GetHashCode();
            byte r = (byte)(v * (0.7f + ((h & 0xFF) / 255f) * 0.3f));
            byte g = (byte)(v * (0.7f + (((h >> 8) & 0xFF) / 255f) * 0.3f));
            byte b = (byte)(v * (0.7f + (((h >> 16) & 0xFF) / 255f) * 0.3f));
            int i = (y * size + x) * 4;
            data[i+0]=r; data[i+1]=g; data[i+2]=b; data[i+3]=255;
        }
        return new Texture(gl, name, size, size, data);
    }

    public void Bind(uint slot = 0)
    {
        _gl.ActiveTexture(TextureUnit.Texture0 + (int)slot);
        _gl.BindTexture(TextureTarget.Texture2D, Handle);
    }

    public void Dispose() => _gl.DeleteTexture(Handle);
}
