using System.Numerics;
using Silk.NET.OpenGL;
using Goreal.Core.ECS;
using Goreal.Core.Components;

namespace Goreal.Core.Graphics;

public sealed class ModelRenderer : IDisposable
{
    readonly GL _gl;
    readonly Shader _shader;
    readonly ModelManager _models;
    readonly Camera _camera;

    public ModelRenderer(GL gl, Shader shader, ModelManager models, Camera camera)
    {
        _gl = gl; _shader = shader; _models = models; _camera = camera;
    }

    public void Render(World world, float aspect)
    {
        _shader.Use();
        _shader.SetMat4("uView", _camera.View);
        _shader.SetMat4("uProj", _camera.Projection(aspect));
        // enable depth
        foreach (var id in world.PropIds)
        {
            var e = new Entity(id);
            ref var prop = ref world.GetProp(e);
            if (string.IsNullOrWhiteSpace(prop.ModelPath)) continue;
            Model model;
            try { model = _models.Get(prop.ModelPath); }
            catch { continue; }

            var translate = Matrix4x4.CreateTranslation(prop.CurrentPos);
            var rotate = Matrix4x4.CreateFromQuaternion(prop.CurrentRot);
            var scale = Matrix4x4.CreateScale(prop.Scale);
            // TrenchBroom angles already in CurrentRot for static? For animated, CurrentRot is animated; for static it's from Angles.
            // Combine: scale * rotate * translate (but we need translate last)
            var modelMat = scale * rotate * translate;
            _shader.SetMat4("uModel", modelMat);
            model.Draw(_shader);
        }
        // reset model
        _shader.SetMat4("uModel", Matrix4x4.Identity);
    }

    public void Dispose() {}
}
