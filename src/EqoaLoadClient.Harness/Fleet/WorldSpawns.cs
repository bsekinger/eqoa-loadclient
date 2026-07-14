using System.Numerics;
using EqoaLoadClient.Core.Movement;

namespace EqoaLoadClient.Harness;

/// A resolved spread spawn: which world the bot joins and its in-cell wander region.
public readonly record struct Spawn(int WorldId, BoundingBoxRegion Region);

/// The random tail of the fleet: bots that spawn in random valid cells across (optionally several)
/// worlds instead of the hubs, so the ladder exercises the whole game, not just the hub cities.
public static class WorldSpawns
{
    /// Parse "0,1,2,..." into the world ids that actually have land cells; falls back to world 0.
    public static int[] ParseWorlds(string spec)
    {
        var worlds = new List<int>();
        foreach (var t in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(t, out int w) && WorldCells.Land.TryGetValue(w, out (int cx, int cz)[]? cells) && cells.Length > 0)
            {
                worlds.Add(w);
            }
        }

        if (worlds.Count == 0)
        {
            worlds.Add(0);
        }

        return worlds.ToArray();
    }

    /// Pick a world weighted by its land-cell count (bigger world => more of the tail), roll in [0,1).
    public static int PickWorld(int[] worlds, double roll)
    {
        long total = 0;
        foreach (int w in worlds)
        {
            total += WorldCells.Land[w].Length;
        }

        double t = roll * total, acc = 0;
        foreach (int w in worlds)
        {
            acc += WorldCells.Land[w].Length;
            if (t < acc)
            {
                return w;
            }
        }

        return worlds[^1];
    }

    /// Deterministic hub/spread split: mask[i] == true means bot i spawns in the random tail.
    public static bool[] SpreadMask(int n, double spreadFraction, int seed)
    {
        var rng = new Random(seed);
        var mask = new bool[n];
        for (int i = 0; i < n; i++)
        {
            mask[i] = rng.NextDouble() < spreadFraction;
        }

        return mask;
    }

    /// A spread spawn: a random valid cell in a cell-count-weighted world, with an in-cell wander box
    /// (cell center +/- wander stays inside the 2000-unit cell, X/Z > 0 => never faults AddObjectToZone).
    public static Spawn Spread(int[] worlds, float y, float wander, int seed)
    {
        var rng = new Random(MixSeed(seed));
        int world = PickWorld(worlds, rng.NextDouble());
        (int cx, int cz)[] cells = WorldCells.Land[world];
        (int cx, int cz) cell = cells[rng.Next(cells.Length)];
        float centerX = cell.cx * 2000 + 1000;
        float centerZ = cell.cz * 2000 + 1000;
        var min = new Vector3(centerX - wander, y, centerZ - wander);
        var max = new Vector3(centerX + wander, y, centerZ + wander);
        var region = new BoundingBoxRegion(min, max, new Vector3(centerX, y, centerZ), seed);
        return new Spawn(world, region);
    }

    private static int MixSeed(int seed)
    {
        uint h = (uint)seed;
        h ^= h >> 16; h *= 0x7feb352du;
        h ^= h >> 15; h *= 0x846ca68bu;
        h ^= h >> 16;
        return (int)h;
    }
}
