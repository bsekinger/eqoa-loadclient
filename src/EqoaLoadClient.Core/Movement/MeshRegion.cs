using System.Numerics;

namespace EqoaLoadClient.Core.Movement;

/// Wanders a box like BoundingBoxRegion, but bound to the client's walkable geometry: spawn and
/// every step sit ON the collision mesh (terrain-correct Y instead of a constant), uphill steps
/// are capped like the client's step-snap, cliffs and steep faces are rejected, and a chest-height
/// segment test keeps bots from clipping through walls. Deterministic for a given seed.
/// Mirrors the client walker's floor-probe model (VIWalkControl_ProbeFloorCeiling, see bridge
/// finding structs/world-esf-collision-bsp.md) at bot fidelity, not physics fidelity.
public sealed class MeshRegion : IMovementRegion
{
    private const float StepUp = 5f;        // max climb per ~10-unit step (cast starts this far above)
    private const float MaxDrop = 12f;      // max descend per step; deeper = a cliff, rejected
    private const float MinNormalY = 0.6f;  // steeper than ~53 degrees is not walkable
    private const float ChestHeight = 3f;   // wall-test segment rides this far above the ground

    private readonly WalkMesh _mesh;
    private readonly Vector2 _min, _max;    // XZ wander box, X/Z clamped >= 1 (emu rejects <= 0)
    private readonly Random _rng;
    private float _heading;

    public Vector3 Spawn { get; }

    public MeshRegion(WalkMesh mesh, Vector2 minXZ, Vector2 maxXZ, Vector2 spawnHint, int seed)
    {
        _mesh = mesh;
        _min = new Vector2(MathF.Max(1f, MathF.Min(minXZ.X, maxXZ.X)), MathF.Max(1f, MathF.Min(minXZ.Y, maxXZ.Y)));
        _max = new Vector2(MathF.Max(minXZ.X, maxXZ.X), MathF.Max(minXZ.Y, maxXZ.Y));
        _rng = new Random(MixSeed(seed));
        _heading = (float)(_rng.NextDouble() * 2 * Math.PI - Math.PI);
        Spawn = FindSpawn(spawnHint);
    }

    public bool Contains(Vector3 p) =>
        p.X >= _min.X && p.X <= _max.X && p.Z >= _min.Y && p.Z <= _max.Y;

    public Vector3 NextStep(Vector3 current, float stepUnits, out float heading)
    {
        for (int attempt = 0; attempt < 16; attempt++)
        {
            float h = attempt == 0
                ? _heading + (float)((_rng.NextDouble() - 0.5) * 0.4)   // gentle wander
                : (float)(_rng.NextDouble() * 2 * Math.PI - Math.PI);    // bounce/re-path
            float nx = current.X + MathF.Cos(h) * stepUnits;
            float nz = current.Z + MathF.Sin(h) * stepUnits;
            if (nx < _min.X || nx > _max.X || nz < _min.Y || nz > _max.Y) continue;

            // Ground at the destination, within step-up/step-down of where we stand.
            if (!_mesh.RaycastDown(nx, nz, current.Y + StepUp, StepUp + MaxDrop, out float ny, out float nrm)) continue;
            if (nrm < MinNormalY) continue;

            // Anything solid between our chest and the destination's chest = a wall; re-path.
            if (_mesh.SegmentBlocked(
                    new Vector3(current.X, current.Y + ChestHeight, current.Z),
                    new Vector3(nx, ny + ChestHeight, nz))) continue;

            _heading = h; heading = h;
            return new Vector3(nx, ny, nz);
        }
        _heading += MathF.PI; heading = _heading;   // boxed in: hold position, turn around
        return current;
    }

    private Vector3 FindSpawn(Vector2 hint)
    {
        float castTop = _mesh.Bounds.Max.Y + 2f;
        float castDepth = castTop - _mesh.Bounds.Min.Y + 4f;

        float hx = Math.Clamp(hint.X, _min.X, _max.X);
        float hz = Math.Clamp(hint.Y, _min.Y, _max.Y);
        if (_mesh.RaycastDown(hx, hz, castTop, castDepth, out float y, out float n) && n >= MinNormalY)
        {
            return new Vector3(hx, y, hz);
        }
        for (int i = 0; i < 512; i++)
        {
            float x = _min.X + (float)_rng.NextDouble() * (_max.X - _min.X);
            float z = _min.Y + (float)_rng.NextDouble() * (_max.Y - _min.Y);
            if (_mesh.RaycastDown(x, z, castTop, castDepth, out y, out n) && n >= MinNormalY)
            {
                return new Vector3(x, y, z);
            }
        }
        throw new InvalidOperationException(
            $"MeshRegion: no walkable ground in box ({_min.X},{_min.Y})..({_max.X},{_max.Y})");
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
