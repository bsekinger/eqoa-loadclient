namespace EqoaLoadClient.Core.Movement;

/// A world's valid roaming area on the X/Z ground plane. Y is a fixed height (grid uses X/Z only).
/// The bot roams anywhere Contains() is true and reflects off everything else — so it is bounded
/// by the *world's valid extent*, not a box around spawn.
public interface IValidArea
{
    /// Overall extent (world units) for spawn sampling and reflection.
    (float minX, float minZ, float maxX, float maxZ) Bounds { get; }
    bool Contains(float x, float z);
}

/// Union of axis-aligned rectangles (world units). Interim bound, e.g. Tunaria's all-land blocks
/// until the full per-world StartZones grid lands.
public sealed class RectUnionArea : IValidArea
{
    private readonly (float minX, float minZ, float maxX, float maxZ)[] _rects;
    public (float minX, float minZ, float maxX, float maxZ) Bounds { get; }

    public RectUnionArea(params (float minX, float minZ, float maxX, float maxZ)[] rects)
    {
        if (rects.Length == 0) throw new ArgumentException("at least one rectangle required", nameof(rects));
        _rects = rects;
        float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
        foreach (var r in rects)
        {
            minX = MathF.Min(minX, r.minX); minZ = MathF.Min(minZ, r.minZ);
            maxX = MathF.Max(maxX, r.maxX); maxZ = MathF.Max(maxZ, r.maxZ);
        }
        Bounds = (minX, minZ, maxX, maxZ);
    }

    public bool Contains(float x, float z)
    {
        foreach (var r in _rects)
            if (x >= r.minX && x <= r.maxX && z >= r.minZ && z <= r.maxZ) return true;
        return false;
    }
}

/// Valid-cell grid matching the server's `_zones[(int)(X/CellSize)][(int)(Z/CellSize)]`. A point is
/// valid iff its 2000-unit cell is a real land zone. This is the authoritative server bound
/// (StartZones dump) — roaming only these cells never faults AddObjectToZone.
public sealed class CellGridArea : IValidArea
{
    public const int CellSize = 2000;
    private readonly HashSet<(int cx, int cz)> _cells;
    public (float minX, float minZ, float maxX, float maxZ) Bounds { get; }

    public CellGridArea(IEnumerable<(int cx, int cz)> validCells)
    {
        _cells = new HashSet<(int, int)>(validCells);
        if (_cells.Count == 0) throw new ArgumentException("at least one valid cell required", nameof(validCells));
        int minCx = int.MaxValue, minCz = int.MaxValue, maxCx = int.MinValue, maxCz = int.MinValue;
        foreach (var (cx, cz) in _cells)
        {
            minCx = Math.Min(minCx, cx); minCz = Math.Min(minCz, cz);
            maxCx = Math.Max(maxCx, cx); maxCz = Math.Max(maxCz, cz);
        }
        Bounds = (minCx * CellSize, minCz * CellSize, (maxCx + 1) * CellSize, (maxCz + 1) * CellSize);
    }

    public bool Contains(float x, float z)
        => x >= 0 && z >= 0 &&
           _cells.Contains(((int)MathF.Floor(x / CellSize), (int)MathF.Floor(z / CellSize)));
}
