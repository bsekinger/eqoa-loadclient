using EqoaLoadClient.Harness;
using Xunit;

public class HubClusteringTests
{
    [Fact]
    public void ParseHubs_reads_coords_and_weights()
    {
        var hubs = HubClustering.ParseHubs("5950,17950,45; 15315,11715,30; 15750,20373,25");
        Assert.Equal(3, hubs.Count);
        Assert.Equal(5950f, hubs[0].X);
        Assert.Equal(17950f, hubs[0].Z);
        Assert.Equal(45.0, hubs[0].Weight);
        Assert.Equal(25.0, hubs[2].Weight);
    }

    [Fact]
    public void ParseHubs_defaults_weight_to_one_when_omitted()
    {
        var hubs = HubClustering.ParseHubs("100,200; 300,400,5");
        Assert.Equal(1.0, hubs[0].Weight);
        Assert.Equal(5.0, hubs[1].Weight);
    }

    [Fact]
    public void ParseHubs_throws_when_no_valid_hub()
    {
        Assert.Throws<ArgumentException>(() => HubClustering.ParseHubs("garbage; also,bad,input,x"));
    }

    [Fact]
    public void AssignHubs_is_deterministic_for_a_seed()
    {
        var hubs = HubClustering.ParseHubs("5950,17950,45; 15315,11715,30; 15750,20373,25");
        var a = HubClustering.AssignHubs(500, hubs, seed: 7);
        var b = HubClustering.AssignHubs(500, hubs, seed: 7);
        Assert.Equal(a, b);
    }

    [Fact]
    public void AssignHubs_distribution_tracks_weights()
    {
        var hubs = HubClustering.ParseHubs("0,0,45; 0,0,30; 0,0,25");
        var assign = HubClustering.AssignHubs(20000, hubs, seed: 1);
        var counts = new int[3];
        foreach (var h in assign)
        {
            counts[h]++;
        }

        // Expect ~45/30/25%; allow +/-3 points for sampling noise at n=20000.
        Assert.InRange(counts[0] / 20000.0, 0.42, 0.48);
        Assert.InRange(counts[1] / 20000.0, 0.27, 0.33);
        Assert.InRange(counts[2] / 20000.0, 0.22, 0.28);
    }

    [Fact]
    public void RegionFor_spawns_inside_the_box_and_positive()
    {
        var hub = new Hub(5950f, 17950f, 45);
        for (int i = 0; i < 200; i++)
        {
            var region = HubClustering.RegionFor(hub, y: 50, jitter: 350, wander: 400, seed: 1000 + i);
            Assert.True(region.Contains(region.Spawn));
            Assert.True(region.Spawn.X > 0 && region.Spawn.Z > 0);
        }
    }
}
