using System.Numerics;

namespace Goreal.Core;

/// <summary>
/// Unit conversion helpers. Quake units -> metres.
/// 1 Quake unit ≈ 0.01905 m ( ~52.5 units per metre ), same scale as Half-Life/Q3.
/// </summary>
public static class Units
{
    public const float QuakeToMeters = 0.01905f;
    public const float MetersToQuake = 1f / QuakeToMeters;
    public const float Deg2Rad = MathF.PI / 180f;
    public const float Rad2Deg = 180f / MathF.PI;
}
