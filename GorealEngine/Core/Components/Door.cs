using System.Numerics;

namespace Goreal.Core.Components;

public struct Door
{
    public Vector3 ClosedPos;   // base offset (0)
    public Vector3 OpenPos;     // offset when open
    public Vector3 CurrentPos;  // current offset
    public Vector3 MoveDir;     // normalized move direction (world)
    public float MoveDistance;  // how far to move (metres)
    public float Speed;         // m/s (converted from Quake units/s)
    public float Wait;          // seconds before auto-close, -1 = stay open
    public float Lip;           // stay lip inside frame
    public bool IsOpen;
    public bool IsMoving;
    public float MoveProgress;  // 0=closed, 1=open
    public int MoveSign;        // +1 opening, -1 closing
    public float WaitTimer;
    public string TargetName;   // if set, only opens via trigger
    public string Target;       // what it triggers when opened
    public bool TriggerOnTouch; // true if no targetname -> touch opens
    public bool UseActivates;   // press E to open
    public float TouchExpand;   // expand AABB for touch detection
}

public struct Trigger
{
    public string Target;       // target to fire
    public string TargetName;   // name that triggers it
    public bool TouchActivates;
    public float Wait;          // cooldown
    public float CooldownTimer;
    public Vector3 Min, Max;    // AABB in world (metres)
}
