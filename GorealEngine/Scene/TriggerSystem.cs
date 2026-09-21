using System.Numerics;
using Goreal.Core.Components;
using Goreal.Core.ECS;

namespace Goreal.Core.Scene;

public sealed class TriggerSystem : ISystem
{
    readonly DoorSystem _doors;
    public TriggerSystem(DoorSystem doors) => _doors = doors;

    public void Update(World world, float dt)
    {
        // find player
        Vector3 pMin = Vector3.Zero, pMax = Vector3.Zero;
        var pe = world.FindPlayer();
        if (pe.HasValue && world.HasTransform(pe.Value))
        {
            ref var tr = ref world.GetTransform(pe.Value);
            if (System.Runtime.CompilerServices.Unsafe.IsNullRef(ref tr)) return;
            var half = new Vector3(16f * Units.QuakeToMeters, 28f * Units.QuakeToMeters, 16f * Units.QuakeToMeters);
            pMin = tr.Position - half;
            pMax = tr.Position + half;
        }
        else return;

        foreach (var tid in world.TriggerIds.ToArray())
        {
            var e = new ECS.Entity(tid);
            ref var t = ref world.GetTrigger(e);
            if (t.CooldownTimer > 0) { t.CooldownTimer -= dt; continue; }

            bool overlap = !(pMax.X < t.Min.X || pMin.X > t.Max.X ||
                             pMax.Y < t.Min.Y || pMin.Y > t.Max.Y ||
                             pMax.Z < t.Min.Z || pMin.Z > t.Max.Z);
            if (!overlap) continue;

            if (!string.IsNullOrEmpty(t.Target))
            {
                foreach (var did in world.DoorIds.ToArray())
                {
                    var de = new ECS.Entity(did);
                    ref var d = ref world.GetDoor(de);
                    if (d.TargetName == t.Target && !d.IsMoving && !d.IsOpen)
                    {
                        d.IsMoving = true; d.MoveSign = 1;
                    }
                }
            }
            t.CooldownTimer = t.Wait > 0 ? t.Wait : 0.5f;
        }
    }
}
