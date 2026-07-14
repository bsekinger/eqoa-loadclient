using System.Numerics;
using EqoaLoadClient.Core.Movement;
using Xunit;

public class BoundingBoxRegionTests
{
    [Fact]
    public void Spawn_is_inside_and_steps_stay_inside()
    {
        var region = new BoundingBoxRegion(
            min: new Vector3(0, 0, 0), max: new Vector3(100, 10, 100),
            spawn: new Vector3(50, 5, 50), seed: 1234);
        Assert.True(region.Contains(region.Spawn));
        var p = region.Spawn;
        for (int i = 0; i < 1000; i++)
        {
            p = region.NextStep(p, stepUnits: 20f, out _);
            Assert.True(region.Contains(p), $"left region at step {i}: {p}");
        }
    }

    [Fact]
    public void Adjacent_seed_bots_wander_in_diverse_directions()
    {
        // Clustered mode builds a BoundingBoxRegion per bot with seed+i. .NET new Random(smallSeed)
        // correlates adjacent seeds, so without decorrelation adjacent bots wander the same way.
        var min = new Vector3(5550, 40, 17550);
        var max = new Vector3(6350, 60, 18350);
        var spawn = new Vector3(5950, 50, 17950);

        var dirs = new List<Vector2>();
        for (int i = 0; i < 60; i++)
        {
            var r = new BoundingBoxRegion(min, max, spawn, seed: i);
            r.NextStep(spawn, stepUnits: 30f, out float h);
            dirs.Add(new Vector2(MathF.Cos(h), MathF.Sin(h)));
        }

        // headings must populate all four quadrants (correlated seeds would clump into one/two)
        Assert.Contains(dirs, d => d.X > 0 && d.Y > 0);
        Assert.Contains(dirs, d => d.X < 0 && d.Y > 0);
        Assert.Contains(dirs, d => d.X > 0 && d.Y < 0);
        Assert.Contains(dirs, d => d.X < 0 && d.Y < 0);
    }
}
