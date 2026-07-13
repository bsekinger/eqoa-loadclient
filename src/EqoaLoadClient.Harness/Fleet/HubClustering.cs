using System.Globalization;
using System.Numerics;
using EqoaLoadClient.Core.Movement;

namespace EqoaLoadClient.Harness;

/// A weighted spawn hub. Population concentrates here in proportion to Weight, mimicking real
/// hub/starter-zone density instead of one unique zone per bot.
public readonly record struct Hub(float X, float Z, double Weight);

/// Assigns bots to hubs by weight and builds an in-hub wander region per bot. Deterministic for a
/// given seed so a scenario is exactly A/B-repeatable across server fixes.
public static class HubClustering
{
    /// Parse "x,z,weight; x,z,weight; ..." (weight optional, defaults to 1).
    public static IReadOnlyList<Hub> ParseHubs(string spec)
    {
        var hubs = new List<Hub>();
        foreach (var part in spec.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var f = part.Split(',', StringSplitOptions.TrimEntries);
            if (f.Length < 2)
            {
                continue;
            }

            if (!float.TryParse(f[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                !float.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
            {
                continue;
            }

            double w = f.Length >= 3 && double.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var wv) && wv > 0
                ? wv
                : 1.0;
            hubs.Add(new Hub(x, z, w));
        }

        if (hubs.Count == 0)
        {
            throw new ArgumentException($"no valid hubs parsed from '{spec}'", nameof(spec));
        }

        return hubs;
    }

    /// Pick a hub index for a roll in [0,1) proportional to weight.
    public static int PickHub(IReadOnlyList<Hub> hubs, double roll)
    {
        double total = 0;
        foreach (var h in hubs)
        {
            total += h.Weight;
        }

        double t = roll * total, acc = 0;
        for (int i = 0; i < hubs.Count; i++)
        {
            acc += hubs[i].Weight;
            if (t < acc)
            {
                return i;
            }
        }

        return hubs.Count - 1;
    }

    /// Deterministically assign each of n bots to a hub index by weight.
    public static int[] AssignHubs(int n, IReadOnlyList<Hub> hubs, int seed)
    {
        var rng = new Random(seed);
        var result = new int[n];
        for (int i = 0; i < n; i++)
        {
            result[i] = PickHub(hubs, rng.NextDouble());
        }

        return result;
    }

    /// In-hub wander region: spawn within +/-jitter of the hub, wander box = hub +/- wander. jitter is
    /// clamped to wander so the spawn is always inside the (in-zone) box, and X/Z stay > 0 (emu rejects <= 0).
    public static BoundingBoxRegion RegionFor(Hub hub, float y, float jitter, float wander, int seed)
    {
        float j = MathF.Min(MathF.Abs(jitter), wander);
        var rng = new Random(MixSeed(seed));
        float sx = MathF.Max(1f, hub.X + (float)(rng.NextDouble() * 2 - 1) * j);
        float sz = MathF.Max(1f, hub.Z + (float)(rng.NextDouble() * 2 - 1) * j);
        var min = new Vector3(MathF.Max(1f, hub.X - wander), y, MathF.Max(1f, hub.Z - wander));
        var max = new Vector3(hub.X + wander, y, hub.Z + wander);
        return new BoundingBoxRegion(min, max, new Vector3(sx, y, sz), seed);
    }

    /// Avalanche-mix (splitmix-style) so sequential per-bot seeds map to well-separated RNG seeds.
    private static int MixSeed(int seed)
    {
        uint h = (uint)seed;
        h ^= h >> 16; h *= 0x7feb352du;
        h ^= h >> 15; h *= 0x846ca68bu;
        h ^= h >> 16;
        return (int)h;
    }
}
