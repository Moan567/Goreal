using System.Numerics;

namespace Goreal.Core.Components;

public struct Transform
{
    public Vector3 Position;
    public Quaternion Rotation;
    public Vector3 Scale;

    public Transform(Vector3 pos)
    {
        Position = pos;
        Rotation = Quaternion.Identity;
        Scale = Vector3.One;
    }

    public Matrix4x4 Matrix => Matrix4x4.CreateScale(Scale)
                               * Matrix4x4.CreateFromQuaternion(Rotation)
                               * Matrix4x4.CreateTranslation(Position);
}

public struct Velocity
{
    public Vector3 Linear;
    public Vector3 Angular;
}

public struct PlayerController
{
    public float Yaw;      // degrees
    public float Pitch;    // degrees, -89..89
    public bool Grounded;
    public bool NoClip;

    public static Vector3 LookDirection(float yawDeg, float pitchDeg)
    {
        float yaw = yawDeg * Units.Deg2Rad;
        float pitch = pitchDeg * Units.Deg2Rad;
        float cy = MathF.Cos(yaw), sy = MathF.Sin(yaw);
        float cp = MathF.Cos(pitch), sp = MathF.Sin(pitch);
        // Forward is -Z in OpenGL, Y up. Yaw around Y.
        return new Vector3(cy * cp, sp, -sy * cp);
    }
}
