using EqoaLoadClient.Core.Movement;
using Xunit;

public class ZoneGridTests
{
    [Fact]
    public void CellOf_maps_to_2000_unit_cells()
    {
        Assert.Equal((2, 8), ZoneGrid.CellOf(5000, 17000));
        Assert.Equal((3, 8), ZoneGrid.CellOf(6001, 17000));
    }

    [Fact]
    public void DistToNearestBorder_finds_nearest_border_and_neighbor()
    {
        // near the high-X border of cell [2,8] (X=6000): 50 units away, neighbor is [3,8]
        float d = ZoneGrid.DistToNearestBorder(5950, 17000, out int ncx, out int ncz);
        Assert.Equal(50f, d, 3);
        Assert.Equal((3, 8), (ncx, ncz));

        // dead center of a cell -> 1000 to the nearest border
        d = ZoneGrid.DistToNearestBorder(5000, 17000, out _, out _);
        Assert.Equal(1000f, d, 3);

        // near the low-Z border of cell [2,8] (Z=16000): 40 units, neighbor [2,7]
        d = ZoneGrid.DistToNearestBorder(5000, 16040, out ncx, out ncz);
        Assert.Equal(40f, d, 3);
        Assert.Equal((2, 7), (ncx, ncz));
    }
}
