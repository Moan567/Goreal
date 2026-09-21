using Goreal.Core.Components;

namespace Goreal.Core.ECS;

public readonly struct Entity : IEquatable<Entity>
{
    public readonly int Id;
    public Entity(int id) => Id = id;
    public bool Equals(Entity other) => Id == other.Id;
    public override bool Equals(object? obj) => obj is Entity e && e.Id == Id;
    public override int GetHashCode() => Id;
    public static implicit operator int(Entity e) => e.Id;
}

public interface ISystem
{
    void Update(World world, float dt);
}

/// <summary>
/// Tiny ECS that stores components in dictionaries and supports the ref-query used by PlayerMoveSystem.
/// Not optimized – fine for a single player.
/// </summary>
public sealed class World
{
    int _nextId = 1;
    readonly Dictionary<int, Transform> _transforms = new();
    readonly Dictionary<int, Velocity> _velocities = new();
    readonly Dictionary<int, PlayerController> _players = new();
    readonly Dictionary<int, Door> _doors = new();
    readonly Dictionary<int, Trigger> _triggers = new();
    readonly Dictionary<int, Prop> _props = new();

    public Entity CreateEntity() => new(_nextId++);

    public void AddComponent(Entity e, Transform c) => _transforms[e.Id] = c;
    public void AddComponent(Entity e, Velocity c) => _velocities[e.Id] = c;
    public void AddComponent(Entity e, PlayerController c) => _players[e.Id] = c;
    public void AddComponent(Entity e, Door c) => _doors[e.Id] = c;
    public void AddComponent(Entity e, Trigger c) => _triggers[e.Id] = c;
    public void AddComponent(Entity e, Prop c) => _props[e.Id] = c;

    public ref Transform GetTransform(Entity e) => ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrNullRef(_transforms, e.Id);
    public ref Velocity GetVelocity(Entity e) => ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrNullRef(_velocities, e.Id);
    public ref PlayerController GetPlayer(Entity e) => ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrNullRef(_players, e.Id);
    public ref Door GetDoor(Entity e) => ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrNullRef(_doors, e.Id);
    public ref Trigger GetTrigger(Entity e) => ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrNullRef(_triggers, e.Id);
    public ref Prop GetProp(Entity e) => ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrNullRef(_props, e.Id);

    public delegate void RefAction<T1, T2, T3>(Entity e, ref T1 a, ref T2 b, ref T3 c)
        where T1 : struct where T2 : struct where T3 : struct;

    public void Query<T1, T2, T3>(RefAction<T1, T2, T3> fn)
        where T1 : struct where T2 : struct where T3 : struct
    {
        // Only one supported triplet for now: Transform, Velocity, PlayerController
        if (typeof(T1) == typeof(Transform) && typeof(T2) == typeof(Velocity) && typeof(T3) == typeof(PlayerController))
        {
            foreach (var kv in _players)
            {
                int id = kv.Key;
                if (!_transforms.ContainsKey(id) || !_velocities.ContainsKey(id)) continue;
                // Need to guarantee refs live – copy via CollectionsMarshal
                ref var t = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrNullRef(_transforms, id);
                ref var v = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrNullRef(_velocities, id);
                ref var p = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrNullRef(_players, id);

                // Cast refs via unsafe reinterpretation: create generic trampoline via dynamic
                // To avoid messy unsafe code, use typed path directly
                var entity = new Entity(id);
                // invoke typed variant
                QueryImpl(entity, ref t, ref v, ref p, fn as RefAction<Transform, Velocity, PlayerController>);
            }
            return;
        }
        throw new NotSupportedException($"Query<{typeof(T1).Name},{typeof(T2).Name},{typeof(T3).Name}> not implemented");
    }

    static void QueryImpl(Entity e, ref Transform t, ref Velocity v, ref PlayerController p, RefAction<Transform, Velocity, PlayerController>? fn)
        => fn?.Invoke(e, ref t, ref v, ref p);

    public Entity? FindPlayer()
    {
        foreach (var id in _players.Keys) return new Entity(id);
        return null;
    }

    public IEnumerable<int> DoorIds => _doors.Keys;
    public IEnumerable<int> TriggerIds => _triggers.Keys;

    public delegate void DoorAction(Entity e, ref Door d);
    public delegate void TriggerAction(Entity e, ref Trigger t);

    public void ForEachDoor(DoorAction fn)
    {
        foreach (var id in _doors.Keys.ToArray())
        {
            ref var d = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrNullRef(_doors, id);
            fn(new Entity(id), ref d);
        }
    }

    public void ForEachTrigger(TriggerAction fn)
    {
        foreach (var id in _triggers.Keys.ToArray())
        {
            ref var tt = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrNullRef(_triggers, id);
            fn(new Entity(id), ref tt);
        }
    }

    public bool HasDoor(Entity e) => _doors.ContainsKey(e.Id);
    public bool HasTransform(Entity e) => _transforms.ContainsKey(e.Id);
    public bool HasPlayer(Entity e) => _players.ContainsKey(e.Id);
    public bool HasProp(Entity e) => _props.ContainsKey(e.Id);
    public IEnumerable<int> PropIds => _props.Keys;
}
