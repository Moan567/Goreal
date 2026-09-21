using System.Numerics;

namespace Goreal.Core.Graphics;

public sealed class Camera
{
    public Vector3 Position;
    public float Yaw, Pitch; // degrees
    public float Fov = 75f;
    public float Near = 0.05f;
    public float Far = 200f;

    public Matrix4x4 View
    {
        get
        {
            var look = LookDir;
            var target = Position + look;
            return Matrix4x4.CreateLookAt(Position, target, Vector3.UnitY);
        }
    }

    public Vector3 LookDir
    {
        get
        {
            float yaw = Yaw * Units.Deg2Rad;
            float pitch = Pitch * Units.Deg2Rad;
            return new Vector3(MathF.Cos(yaw) * MathF.Cos(pitch), MathF.Sin(pitch), -MathF.Sin(yaw) * MathF.Cos(pitch));
        }
    }

    public Matrix4x4 Projection(float aspect) =>
        Matrix4x4.CreatePerspectiveFieldOfView(Fov * Units.Deg2Rad, aspect, Near, Far);
}
