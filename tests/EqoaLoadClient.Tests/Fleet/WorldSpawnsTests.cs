using EqoaLoadClient.Harness;
using Xunit;

public class WorldSpawnsTests
{
    [Fact]
    public void WorldCells_has_all_six_worlds_and_known_hub_cells()
    {
        for (int w = 0; w <= 5; w++)
        {
            Assert.True(WorldCells.Land.ContainsKey(w), $"world {w} missing");
            Assert.NotEmpty(WorldCells.Land[w]);
        }

        // Tunaria (world 0) must contain the verified starting-city cells.
        Assert.Contains((2, 8), WorldCells.Land[0]);   // Qeynos
        Assert.Contains((12, 7), WorldCells.Land[0]);  // Freeport
        Assert.Contains((6, 2), WorldCells.Land[0]);   // Halas
    }

    [Fact]
    public void ParseWorlds_keeps_only_worlds_with_land()
    {
        var w = WorldSpawns.ParseWorlds("0,1,2,3,4,5");
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, w);
        Assert.Equal(new[] { 0 }, WorldSpawns.ParseWorlds("garbage"));   // falls back to world 0
    }

    [Fact]
    public void PickWorld_weights_by_cell_count()
    {
        var worlds = new[] { 0, 1, 2, 3, 4, 5 };
        var counts = new int[6];
        var rng = new Random(42);
        for (int i = 0; i < 20000; i++)
        {
            counts[WorldSpawns.PickWorld(worlds, rng.NextDouble())]++;
        }

        // Tunaria (138 land cells) must dominate; the 2-cell worlds must be rare but non-zero.
        Assert.True(counts[0] > counts[1] && counts[1] > counts[3]);
        Assert.InRange(counts[0] / 20000.0, 0.70, 0.84);   // 138 / 176 ≈ 0.78
    }

    [Fact]
    public void SpreadMask_fraction_is_approximately_right()
    {
        var mask = WorldSpawns.SpreadMask(10000, 0.25, seed: 7);
        int spread = 0;
        foreach (var b in mask)
        {
            if (b) spread++;
        }

        Assert.InRange(spread / 10000.0, 0.22, 0.28);
    }

    [Fact]
    public void Spread_lands_in_a_valid_cell_and_region_contains_spawn()
    {
        var worlds = WorldSpawns.ParseWorlds("0,1,2,3,4,5");
        for (int i = 0; i < 400; i++)
        {
            var s = WorldSpawns.Spread(worlds, y: 50, wander: 400, seed: 5000 + i);
            Assert.True(s.Region.Contains(s.Region.Spawn));
            Assert.True(s.Region.Spawn.X > 0 && s.Region.Spawn.Z > 0);

            // The spawn's cell must be a real land cell in that world.
            int cx = (int)(s.Region.Spawn.X / 2000);
            int cz = (int)(s.Region.Spawn.Z / 2000);
            Assert.Contains((cx, cz), WorldCells.Land[s.WorldId]);
        }
    }
}
