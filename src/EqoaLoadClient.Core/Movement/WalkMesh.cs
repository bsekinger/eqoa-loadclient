using System.Numerics;

namespace EqoaLoadClient.Core.Movement;

/// Client-derived collision triangle soup with an XZ grid index. Geometry comes from the
/// per-zone ECOL (.col) files exported from the client .esf collision chunks (0x4200 strips +
/// 0x3220 BSP — see bridge finding structs/world-esf-collision-bsp.md). Only the FILE format is
/// shared with the emu; the queries here are an independent implementation. Triangles are
/// double-sided (winding is meaningless in the source data). Queries are read-only and safe to
/// share across bots/threads.
public sealed class WalkMesh
{
    private readonly float[] _verts;   // xyz triples
    private readonly int[] _tris;      // vertex-index triples
    private readonly int[][] _cells;   // per grid cell: tri indices whose XZ AABB overlaps it
    private readonly float _cellSize;
    private readonly float _minX, _minZ;
    private readonly int _nx, _nz;

    public int TriCount => _tris.Length / 3;
    public (Vector3 Min, Vector3 Max) Bounds { get; }

    public WalkMesh(float[] verts, int[] tris, float cellSize = 64f)
    {
        _verts = verts; _tris = tris; _cellSize = cellSize;

        var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue);
        for (int i = 0; i < verts.Length; i += 3)
        {
            min = Vector3.Min(min, new Vector3(verts[i], verts[i + 1], verts[i + 2]));
            max = Vector3.Max(max, new Vector3(verts[i], verts[i + 1], verts[i + 2]));
        }
        if (verts.Length == 0) { min = Vector3.Zero; max = Vector3.Zero; }
        Bounds = (min, max);
        _minX = min.X; _minZ = min.Z;
        _nx = Math.Max(1, (int)MathF.Ceiling((max.X - min.X) / cellSize));
        _nz = Math.Max(1, (int)MathF.Ceiling((max.Z - min.Z) / cellSize));

        var buckets = new List<int>[_nx * _nz];
        for (int t = 0; t < TriCount; t++)
        {
            (float txMin, float tzMin, float txMax, float tzMax) = TriAabbXZ(t);
            (int cx0, int cz0) = CellOf(txMin, tzMin);
            (int cx1, int cz1) = CellOf(txMax, tzMax);
            for (int cz = cz0; cz <= cz1; cz++)
            {
                for (int cx = cx0; cx <= cx1; cx++)
                {
                    (buckets[cz * _nx + cx] ??= new List<int>()).Add(t);
                }
            }
        }
        _cells = new int[buckets.Length][];
        for (int i = 0; i < buckets.Length; i++)
        {
            _cells[i] = buckets[i]?.ToArray() ?? Array.Empty<int>();
        }
    }

    /// Vertical ray straight down from (x, fromY, z), at most maxDrop deep. Returns the HIGHEST
    /// ground at or below fromY (so decks/bridges win over the terrain beneath them) and the
    /// unsigned Y of the surface's unit normal (1 = flat, 0 = vertical).
    public bool RaycastDown(float x, float z, float fromY, float maxDrop, out float groundY, out float normalY)
    {
        groundY = float.MinValue; normalY = 0;
        (int cx, int cz) = CellOf(x, z);
        foreach (int t in _cells[cz * _nx + cx])
        {
            int i0 = _tris[t * 3] * 3, i1 = _tris[t * 3 + 1] * 3, i2 = _tris[t * 3 + 2] * 3;
            float ax = _verts[i0], ay = _verts[i0 + 1], az = _verts[i0 + 2];
            float bx = _verts[i1], by = _verts[i1 + 1], bz = _verts[i1 + 2];
            float cx2 = _verts[i2], cy = _verts[i2 + 1], cz2 = _verts[i2 + 2];

            // 2D edge functions in XZ; all-same-sign (or zero) = inside. Vertical tris have ~zero
            // area in XZ and never contain the point, so walls are naturally skipped as "ground".
            float d0 = (bx - ax) * (z - az) - (bz - az) * (x - ax);
            float d1 = (cx2 - bx) * (z - bz) - (cz2 - bz) * (x - bx);
            float d2 = (ax - cx2) * (z - cz2) - (az - cz2) * (x - cx2);
            bool hasNeg = d0 < 0 || d1 < 0 || d2 < 0;
            bool hasPos = d0 > 0 || d1 > 0 || d2 > 0;
            if (hasNeg && hasPos) continue;

            float nx = (by - ay) * (cz2 - az) - (bz - az) * (cy - ay);
            float nyRaw = (bz - az) * (cx2 - ax) - (bx - ax) * (cz2 - az);
            float nz = (bx - ax) * (cy - ay) - (by - ay) * (cx2 - ax);
            if (MathF.Abs(nyRaw) < 1e-6f) continue;

            float y = ay - (nx * (x - ax) + nz * (z - az)) / nyRaw;
            if (y > fromY || y < fromY - maxDrop || y <= groundY) continue;

            groundY = y;
            normalY = MathF.Abs(nyRaw) / MathF.Sqrt(nx * nx + nyRaw * nyRaw + nz * nz);
        }
        return groundY > float.MinValue;
    }

    /// True if the straight segment a->b intersects any triangle (used as a wall test at chest
    /// height). Double-sided Möller–Trumbore over the grid cells the segment's XZ AABB overlaps.
    public bool SegmentBlocked(Vector3 a, Vector3 b)
    {
        Vector3 dir = b - a;
        (int cx0, int cz0) = CellOf(MathF.Min(a.X, b.X), MathF.Min(a.Z, b.Z));
        (int cx1, int cz1) = CellOf(MathF.Max(a.X, b.X), MathF.Max(a.Z, b.Z));
        for (int cz = cz0; cz <= cz1; cz++)
        {
            for (int cx = cx0; cx <= cx1; cx++)
            {
                foreach (int t in _cells[cz * _nx + cx])
                {
                    if (SegmentHitsTri(a, dir, t)) return true;
                }
            }
        }
        return false;
    }

    private bool SegmentHitsTri(Vector3 origin, Vector3 dir, int t)
    {
        int i0 = _tris[t * 3] * 3, i1 = _tris[t * 3 + 1] * 3, i2 = _tris[t * 3 + 2] * 3;
        var v0 = new Vector3(_verts[i0], _verts[i0 + 1], _verts[i0 + 2]);
        var e1 = new Vector3(_verts[i1], _verts[i1 + 1], _verts[i1 + 2]) - v0;
        var e2 = new Vector3(_verts[i2], _verts[i2 + 1], _verts[i2 + 2]) - v0;

        Vector3 p = Vector3.Cross(dir, e2);
        float det = Vector3.Dot(e1, p);
        if (MathF.Abs(det) < 1e-9f) return false;      // parallel (double-sided: no backface cull)
        float inv = 1f / det;
        Vector3 s = origin - v0;
        float u = Vector3.Dot(s, p) * inv;
        if (u < 0 || u > 1) return false;
        Vector3 q = Vector3.Cross(s, e1);
        float v = Vector3.Dot(dir, q) * inv;
        if (v < 0 || u + v > 1) return false;
        float tt = Vector3.Dot(e2, q) * inv;
        return tt > 1e-4f && tt < 1f - 1e-4f;
    }

    private (float, float, float, float) TriAabbXZ(int t)
    {
        int i0 = _tris[t * 3] * 3, i1 = _tris[t * 3 + 1] * 3, i2 = _tris[t * 3 + 2] * 3;
        return (
            MathF.Min(_verts[i0], MathF.Min(_verts[i1], _verts[i2])),
            MathF.Min(_verts[i0 + 2], MathF.Min(_verts[i1 + 2], _verts[i2 + 2])),
            MathF.Max(_verts[i0], MathF.Max(_verts[i1], _verts[i2])),
            MathF.Max(_verts[i0 + 2], MathF.Max(_verts[i1 + 2], _verts[i2 + 2])));
    }

    private (int, int) CellOf(float x, float z)
    {
        int cx = Math.Clamp((int)((x - _minX) / _cellSize), 0, _nx - 1);
        int cz = Math.Clamp((int)((z - _minZ) / _cellSize), 0, _nz - 1);
        return (cx, cz);
    }

    // ---- ECOL (.col) loading -------------------------------------------------------------

    private const uint EcolMagic = 0x4C4F4345; // "ECOL"

    /// Loads and merges the given ECOL files into one soup (they are already world-space).
    public static WalkMesh LoadEcolFiles(IEnumerable<string> paths, float cellSize = 64f)
    {
        var verts = new List<float>();
        var tris = new List<int>();
        foreach (string path in paths)
        {
            using var r = OpenEcol(path, out int vertCount);
            int baseVert = verts.Count / 3;
            for (int i = 0; i < vertCount * 3; i++) verts.Add(r.ReadSingle());
            r.BaseStream.Seek(24, SeekOrigin.Current);            // bmin/bmax
            r.ReadInt32();                                        // maxTrisPerChunk
            int nnodes = r.ReadInt32();
            r.BaseStream.Seek(nnodes * 24L, SeekOrigin.Current);  // prebuilt tree: unused here
            int triCount = r.ReadInt32();
            for (int i = 0; i < triCount * 3; i++) tris.Add(r.ReadInt32() + baseVert);
        }
        return new WalkMesh(verts.ToArray(), tris.ToArray(), cellSize);
    }

    /// XZ bounds of an ECOL file from its header (verts are skipped, not read).
    public static (Vector3 Min, Vector3 Max) ReadEcolBounds(string path)
    {
        using var r = OpenEcol(path, out int vertCount);
        r.BaseStream.Seek(vertCount * 12L, SeekOrigin.Current);
        return (new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
                new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()));
    }

    /// Loads every "<prefix>_*.col" in dir whose XZ bounds intersect [minXZ-margin, maxXZ+margin];
    /// null when none do (caller should fall back to a non-mesh region).
    public static WalkMesh? LoadForBounds(string dir, string prefix, Vector2 minXZ, Vector2 maxXZ,
        float margin = 100f, float cellSize = 64f)
    {
        var hits = new List<string>();
        foreach (string path in Directory.GetFiles(dir, prefix + "_*.col"))
        {
            var (bmin, bmax) = ReadEcolBounds(path);
            if (bmax.X >= minXZ.X - margin && bmin.X <= maxXZ.X + margin &&
                bmax.Z >= minXZ.Y - margin && bmin.Z <= maxXZ.Y + margin)
            {
                hits.Add(path);
            }
        }
        return hits.Count == 0 ? null : LoadEcolFiles(hits, cellSize);
    }

    private static BinaryReader OpenEcol(string path, out int vertCount)
    {
        var r = new BinaryReader(File.OpenRead(path));
        if (r.ReadUInt32() != EcolMagic) { r.Dispose(); throw new InvalidDataException($"{path}: not an ECOL file"); }
        ushort ver = r.ReadUInt16(); r.ReadUInt16();
        if (ver != 1) { r.Dispose(); throw new InvalidDataException($"{path}: ECOL version {ver} unsupported"); }
        vertCount = r.ReadInt32();
        return r;
    }
}
