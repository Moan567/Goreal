using System.Numerics;
using System.Runtime.CompilerServices;
using Goreal.Core;
using Goreal.Core.Components;
using Goreal.Core.ECS;
using Goreal.Core.Physics;
using Silk.NET.Input;

namespace Goreal.Core.Scene;

public sealed class DoorSystem : ISystem
{
    readonly CollisionWorld _collision;
    readonly InputState _input;
    readonly Dictionary<int, List<CollisionBrushData>> _doorBrushes = new();
    readonly Dictionary<int, int> _handles = new(); // entity id -> dynamic handle
    readonly Dictionary<int, (Vector3 min, Vector3 max)> _doorBounds = new();

    public DoorSystem(CollisionWorld collision, InputState input)
    {
        _collision = collision;
        _input = input;
    }

    public void RegisterDoor(Entity e, List<CollisionBrushData> brushes, Vector3 min, Vector3 max, int handle)
    {
        _doorBrushes[e.Id] = brushes;
        _handles[e.Id] = handle;
        _doorBounds[e.Id] = (min, max);
    }

    public void Update(World world, float dt)
    {
        // find player pos for touch checks
        Vector3 playerPos = Vector3.Zero;
        Vector3 playerHalf = new(0.3f, 0.9f, 0.3f);
        float playerYaw = 0, playerPitch = 0;
        var pe = world.FindPlayer();
        if (pe.HasValue && world.HasTransform(pe.Value))
        {
            ref var tr = ref world.GetTransform(pe.Value);
            ref var pc = ref world.GetPlayer(pe.Value);
            // null-ref check (CollectionsMarshal returns null ref if missing)
            if (!Unsafe.IsNullRef(ref tr))
            {
                playerPos = tr.Position;
                playerYaw = pc.Yaw;
                playerPitch = pc.Pitch;
                playerHalf = new(16f * Units.QuakeToMeters, 28f * Units.QuakeToMeters, 16f * Units.QuakeToMeters);
            }
        }
        bool usePressed = _input.WasPressed(Key.E);

        foreach (var id in world.DoorIds.ToArray())
        {
            var e = new Entity(id);
            ref var d = ref world.GetDoor(e);
            bool wasMoving = d.IsMoving;

            // movement update
            if (d.IsMoving)
            {
                float dist = d.MoveDistance;
                if (dist < 0.01f) { d.IsMoving = false; d.MoveProgress = d.IsOpen ? 1f : 0f; }
                else
                {
                    float speed = d.Speed; // m/s
                    float delta = speed * dt / dist;
                    d.MoveProgress += delta * d.MoveSign;
                    if (d.MoveProgress >= 1f) { d.MoveProgress = 1f; d.IsMoving = false; d.IsOpen = true; d.WaitTimer = d.Wait; }
                    else if (d.MoveProgress <= 0f) { d.MoveProgress = 0f; d.IsMoving = false; d.IsOpen = false; }
                }
                d.CurrentPos = d.ClosedPos + d.MoveDir * d.MoveDistance * d.MoveProgress;
                if (_handles.TryGetValue(e.Id, out var h)) _collision.SetDynamicOffset(h, d.CurrentPos);
            }
            else
            {
                // auto-close after wait
                if (d.IsOpen && d.Wait >= 0)
                {
                    d.WaitTimer -= dt;
                    if (d.WaitTimer <= 0f)
                    {
                        d.IsMoving = true; d.MoveSign = -1;
                    }
                }
                else if (!d.IsOpen)
                {
                    bool shouldOpen = false;
                    // touch check (if no targetname or TriggerOnTouch)
                    if (d.TriggerOnTouch && string.IsNullOrEmpty(d.TargetName))
                    {
                        // expand bounds a bit for touch
                        if (_doorBounds.TryGetValue(e.Id, out var b))
                        {
                            Vector3 pMin = playerPos - playerHalf;
                            Vector3 pMax = playerPos + playerHalf;
                            // door bounds at closed pos expanded
                            Vector3 dMin = b.min + d.ClosedPos - new Vector3(0.5f);
                            Vector3 dMax = b.max + d.ClosedPos + new Vector3(0.5f);
                            bool overlap = !(pMax.X < dMin.X || pMin.X > dMax.X || pMax.Y < dMin.Y || pMin.Y > dMax.Y || pMax.Z < dMin.Z || pMin.Z > dMax.Z);
                            if (overlap) shouldOpen = true;
                        }
                    }
                    // use key when looking at door
                    if (usePressed && d.UseActivates)
                    {
                        // ray from eye
                        Vector3 eye = playerPos + new Vector3(0, 0.55f, 0);
                        Vector3 look = PlayerController.LookDirection(playerYaw, playerPitch);
                        Vector3 end = eye + look * 2.0f;
                        var tr = _collision.Trace(eye, end, Vector3.Zero);
                        // if we hit this door's brushes, need to detect which brush was hit – for now any hit within 2m triggers nearest door
                        // simplified: check distance to door center
                        if (_doorBounds.TryGetValue(e.Id, out var b))
                        {
                            Vector3 center = (b.min + b.max) * 0.5f + d.ClosedPos;
                            float dist = Vector3.Distance(eye, center);
                            if (dist < 3.0f)
                            {
                                // do a small sphere check: if ray passes within door bounds
                                // cheap: check if end point is near
                                shouldOpen = true;
                            }
                        }
                    }
                    if (shouldOpen)
                    {
                        d.IsMoving = true; d.MoveSign = 1;
                        // if was open and waiting to close, restart open
                    }
                }
            }

            // ensure offset applied even if not moving (initial)
            if (!wasMoving && !d.IsMoving && _handles.TryGetValue(e.Id, out var handle))
            {
                _collision.SetDynamicOffset(handle, d.CurrentPos);
            }
        }
    }

    public bool TryTrigger(string targetName)
    {
        bool any = false;
        // find doors/triggers that have TargetName == targetName
        // This will be called by TriggerSystem or external
        return any;
    }
}
