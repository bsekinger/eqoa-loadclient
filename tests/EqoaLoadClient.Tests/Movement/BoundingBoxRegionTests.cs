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
}
