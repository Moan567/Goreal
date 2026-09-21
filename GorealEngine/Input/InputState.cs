using System.Numerics;
using Silk.NET.Input;

namespace Goreal.Core;

/// <summary>
/// Pollable input snapshot fed to PlayerMoveSystem. Updated each frame from Silk.NET.
/// </summary>
public sealed class InputState
{
    readonly HashSet<Key> _down = new();
    readonly HashSet<Key> _pressedThisFrame = new();

    public Vector2 MouseDelta { get; private set; }
    public bool MouseCaptured { get; set; } = true;

    public void SetKey(Key key, bool down)
    {
        if (down)
        {
            if (_down.Add(key)) _pressedThisFrame.Add(key);
        }
        else _down.Remove(key);
    }

    public void AddMouseDelta(Vector2 d) => MouseDelta += d;
    public void EndFrame()
    {
        _pressedThisFrame.Clear();
        MouseDelta = Vector2.Zero;
    }

    public bool IsDown(Key key) => _down.Contains(key);
    public bool WasPressed(Key key) => _pressedThisFrame.Contains(key);
}
