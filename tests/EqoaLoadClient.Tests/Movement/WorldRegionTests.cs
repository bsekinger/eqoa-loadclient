using System.Numerics;
using EqoaLoadClient.Core.Movement;
using Xunit;

public class WorldRegionTests
{
    // Tunaria's two all-land blocks (same as WorldBounds).
    private static IValidArea Tunaria() =>
        new RectUnionArea((4000f, 4000f, 12000f, 22000f), (12000f, 4000f, 20000f, 32000f));

    [Fact]
    public void Roaming_stays_in_valid_area_and_crosses_many_cells()
    {
        var region = new WorldRegion(Tunaria(), y: 50f, seed: 3, fixedSpawn: new Vector3(5000, 50, 17000));
        var cells = new HashSet<(int, int)>();
        var p = region.Spawn;
        Assert.True(region.Contains(p));

        for (int i = 0; i < 5000; i++)
        {
            p = region.NextStep(p, stepUnits: 200f, out _);
            Assert.True(region.Contains(p), $"left valid area at {p}");
            Assert.Equal(50f, p.Y);                       // Y pinned to spawn height
            cells.Add(((int)MathF.Floor(p.X / 2000), (int)MathF.Floor(p.Z / 2000)));
        }
        Assert.True(cells.Count >= 4, $"expected to cross zone borders; visited {cells.Count} cells");
    }

    [Fact]
    public void Never_enters_an_invalid_cell()
    {
        var area = new CellGridArea(new[] { (2, 8) });    // only Qeynos cell is valid
        var region = new WorldRegion(area, 50f, 7, new Vector3(5000, 50, 17000));
        var p = region.Spawn;
        for (int i = 0; i < 2000; i++)
        {
            p = region.NextStep(p, 300f, out _);
            Assert.True(area.Contains(p.X, p.Z), $"entered invalid cell at {p}");
        }
    }

    [Fact]
    public void Random_spawn_is_always_valid()
    {
        for (int seed = 0; seed < 25; seed++)
            Assert.True(new WorldRegion(Tunaria(), 50f, seed).Contains(new WorldRegion(Tunaria(), 50f, seed).Spawn));
    }

    [Fact]
    public void Fixed_spawn_used_when_valid_else_falls_back_to_random_valid()
    {
        var valid = new WorldRegion(Tunaria(), 50f, 1, new Vector3(5000, 50, 17000));
        Assert.Equal(new Vector3(5000, 50, 17000), valid.Spawn);

        var oob = new WorldRegion(Tunaria(), 50f, 1, new Vector3(-9999, 50, -9999));
        Assert.True(valid.Contains(oob.Spawn));           // invalid fixed spawn -> random valid point
    }

    [Fact]
    public void CellGridArea_maps_points_to_2000_unit_cells()
    {
        var area = new CellGridArea(new[] { (2, 8), (3, 8) });
        Assert.True(area.Contains(5000, 17000));          // cell [2,8]
        Assert.True(area.Contains(6500, 17000));          // cell [3,8]
        Assert.False(area.Contains(9000, 17000));         // cell [4,8] not listed
        Assert.False(area.Contains(5000, 3000));          // cell [2,1] not listed
    }

    [Fact]
    public void RectUnionArea_is_the_union_of_its_rectangles()
    {
        var area = Tunaria();
        Assert.True(area.Contains(5000, 17000));           // block A
        Assert.True(area.Contains(18000, 30000));          // block B (Z>22000 => only B)
        Assert.False(area.Contains(3000, 3000));           // below both blocks
        Assert.False(area.Contains(5000, 30000));          // X in A's range but Z>22000 and X<12000 => outside both
        Assert.Equal((4000f, 4000f, 20000f, 32000f), area.Bounds);
    }
}
