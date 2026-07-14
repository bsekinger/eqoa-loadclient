using System.Numerics;
namespace EqoaLoadClient.Core.Movement;

public sealed class BoundingBoxRegion : IMovementRegion
{
    private readonly Vector3 _min, _max;
    private readonly Random _rng;
    private float _heading;

    public Vector3 Spawn { get; }

    public BoundingBoxRegion(Vector3 min, Vector3 max, Vector3 spawn, int seed)
    {
        _min = Vector3.Min(min, max); _max = Vector3.Max(min, max);
        Spawn = Vector3.Clamp(spawn, _min, _max);
        // Decorrelate: new Random(1), new Random(2)... yield correlated first values, so adjacent-index
        // bots (seed+i) would wander the same way. Avalanche-mix the seed (as WorldRegion does).
        _rng = new Random(MixSeed(seed));
        _heading = (float)(_rng.NextDouble() * 2 * Math.PI - Math.PI);
    }

    public bool Contains(Vector3 p) =>
        p.X >= _min.X && p.X <= _max.X && p.Y >= _min.Y && p.Y <= _max.Y &&
        p.Z >= _min.Z && p.Z <= _max.Z;

    public Vector3 NextStep(Vector3 current, float stepUnits, out float heading)
    {
        // Occasional heading jitter -> plausible wander; reflect off the walls.
        _heading += (float)((_rng.NextDouble() - 0.5) * 0.6);
        var dir = new Vector3(MathF.Cos(_heading), 0, MathF.Sin(_heading));
        var next = current + dir * stepUnits;
        if (next.X < _min.X || next.X > _max.X) { _heading = MathF.PI - _heading; }
        if (next.Z < _min.Z || next.Z > _max.Z) { _heading = -_heading; }
        dir = new Vector3(MathF.Cos(_heading), 0, MathF.Sin(_heading));
        next = Vector3.Clamp(current + dir * stepUnits, _min, _max);
        heading = _heading;
        return next;
    }

    /// Avalanche hash (splitmix-style) so sequential per-bot seeds map to well-separated RNG seeds.
    private static int MixSeed(int seed)
    {
        uint h = (uint)seed;
        h ^= h >> 16; h *= 0x7feb352du;
        h ^= h >> 15; h *= 0x846ca68bu;
        h ^= h >> 16;
        return (int)h;
    }
}
