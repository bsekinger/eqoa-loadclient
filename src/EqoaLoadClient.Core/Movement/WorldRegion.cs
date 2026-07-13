using System.Numerics;

namespace EqoaLoadClient.Core.Movement;

/// Roams the full valid area of a world, reflecting off invalid cells / the world edge. The bot is
/// bounded only by the world's valid extent (not a spawn box), so it sweeps across many 2000-unit
/// cells — crossing zone borders to trigger server zone loads. Y stays at the spawn height.
public sealed class WorldRegion : IMovementRegion
{
    private readonly IValidArea _area;
    private readonly float _y;
    private readonly Random _rng;
    private float _heading;

    public Vector3 Spawn { get; }

    /// <param name="fixedSpawn">If given AND valid, spawn there; otherwise pick a random valid point
    /// (spreads a fleet across the whole world).</param>
    public WorldRegion(IValidArea area, float y, int seed, Vector3? fixedSpawn = null)
    {
        _area = area; _y = y;
        // Decorrelate: new Random(1), new Random(2)... yield correlated first values, so adjacent-index
        // bots would spawn together and head the same way. Avalanche-mix the seed so every bot differs.
        _rng = new Random(MixSeed(seed));
        _heading = (float)(_rng.NextDouble() * 2 * Math.PI - Math.PI);
        Spawn = fixedSpawn is Vector3 s && _area.Contains(s.X, s.Z)
            ? new Vector3(s.X, y, s.Z)
            : RandomValidPoint();
    }

    public bool Contains(Vector3 p) => _area.Contains(p.X, p.Z);

    public Vector3 NextStep(Vector3 current, float stepUnits, out float heading)
    {
        // Try the current heading (gentle wander); if the step would leave the valid area, bounce by
        // retrying random headings. Robust for arbitrary (non-convex) valid areas, not just boxes.
        for (int attempt = 0; attempt < 16; attempt++)
        {
            float h = attempt == 0
                ? _heading + (float)((_rng.NextDouble() - 0.5) * 0.4)     // small course change
                : (float)(_rng.NextDouble() * 2 * Math.PI - Math.PI);      // bounce off the wall
            var next = current + new Vector3(MathF.Cos(h), 0, MathF.Sin(h)) * stepUnits;
            if (_area.Contains(next.X, next.Z))
            {
                _heading = h; heading = _heading;
                return new Vector3(next.X, _y, next.Z);
            }
        }
        // Boxed in (only if the valid area is a single sub-step cell): hold position, flip heading.
        _heading += MathF.PI; heading = _heading;
        return new Vector3(current.X, _y, current.Z);
    }

    private Vector3 RandomValidPoint()
    {
        var (minX, minZ, maxX, maxZ) = _area.Bounds;
        for (int i = 0; i < 2000; i++)
        {
            float x = minX + (float)_rng.NextDouble() * (maxX - minX);
            float z = minZ + (float)_rng.NextDouble() * (maxZ - minZ);
            if (_area.Contains(x, z)) return new Vector3(x, _y, z);
        }
        return new Vector3((minX + maxX) / 2, _y, (minZ + maxZ) / 2);   // fallback: extent center
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
