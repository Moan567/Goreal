using System.Numerics;

namespace Goreal.Core.Components;

public enum PropMoverType
{
    Static,         // misc_model
    Bob,            // sin y
    Rotate,         // func_rotating  (yaw)
    MoveLinear,     // func_plat / func_movelinear (back-forth)
    Path            // future
}

public struct Prop
{
    public string ModelPath;      // e.g. "models/crate.obj"
    public Vector3 Origin;        // world pos (metres)
    public Vector3 Angles;        // euler degrees (pitch yaw roll) – trenchbroom "angles"
    public Vector3 Scale;
    public PropMoverType Mover;
    public Vector3 MoveDir;       // for MoveLinear
    public float MoveDistance;    // metres
    public float Speed;           // for rotate deg/s or move m/s or bob Hz
    public float BobHeight;       // for Bob
    public float CurrentTime;     // animation time
    public Vector3 CurrentPos;    // animated pos (for collision)
    public Quaternion CurrentRot; // animated rot
    public bool CastsShadow;
}
