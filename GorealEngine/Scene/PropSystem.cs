using System.Numerics;
using Goreal.Core.Components;
using Goreal.Core.ECS;
using Goreal.Core.Physics;

namespace Goreal.Core.Scene;

public sealed class PropSystem : ISystem
{
    readonly CollisionWorld _collision;
    readonly Dictionary<int, (List<CollisionBrushData> brushes, int handle, Vector3 baseMin, Vector3 baseMax)> _brushMovers = new();

    public PropSystem(CollisionWorld collision) => _collision = collision;

    public void RegisterBrushMover(Entity e, List<CollisionBrushData> brushes, Vector3 min, Vector3 max, int handle)
    {
        _brushMovers[e.Id] = (brushes, handle, min, max);
    }

    public void Update(World world, float dt)
    {
        foreach (var id in world.PropIds.ToArray())
        {
            var e = new Entity(id);
            ref var prop = ref world.GetProp(e);
            prop.CurrentTime += dt;

            switch (prop.Mover)
            {
                case PropMoverType.Bob:
                {
                    float y = MathF.Sin(prop.CurrentTime * prop.Speed * MathF.Tau) * prop.BobHeight;
                    prop.CurrentPos = prop.Origin + new Vector3(0, y, 0);
                    // no collision for bob point prop (just visual)
                    break;
                }
                case PropMoverType.Rotate:
                {
                    float yaw = prop.CurrentTime * prop.Speed; // deg/s
                    prop.CurrentRot = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw * MathF.PI / 180f);
                    // also handle pitch/roll from Angles? keep base
                    if (_brushMovers.TryGetValue(id, out var bm))
                    {
                        // for brush rotating, we would need to rotate collision – for now just translate 0
                        // TODO: rotate planes if needed
                        _collision.SetDynamicOffset(bm.handle, Vector3.Zero);
                    }
                    break;
                }
                case PropMoverType.MoveLinear:
                {
                    // ping-pong 0->1->0
                    float period = 0;
                    if (prop.MoveDistance > 0.01f && prop.Speed > 0) period = prop.MoveDistance * 2f / prop.Speed;
                    float t = 0;
                    if (period > 0)
                    {
                        float phase = (prop.CurrentTime % period) / period; // 0..1 (0->0.5 forward, 0.5->1 back)
                        t = phase < 0.5f ? phase * 2f : (1f - phase) * 2f;
                    }
                    prop.CurrentPos = prop.Origin + prop.MoveDir * prop.MoveDistance * t;
                    if (_brushMovers.TryGetValue(id, out var bm))
                        _collision.SetDynamicOffset(bm.handle, prop.CurrentPos - prop.Origin);
                    break;
                }
                case PropMoverType.Static:
                default:
                    prop.CurrentPos = prop.Origin;
                    prop.CurrentRot = Quaternion.CreateFromYawPitchRoll(prop.Angles.Y * MathF.PI/180f, prop.Angles.X * MathF.PI/180f, prop.Angles.Z * MathF.PI/180f);
                    break;
            }
        }
    }
}
