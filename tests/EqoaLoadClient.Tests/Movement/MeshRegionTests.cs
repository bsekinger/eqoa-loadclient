using System.Numerics;
using EqoaLoadClient.Core.Movement;
using Xunit;

public class MeshRegionTests
{
    // Terrain: plane y=10 over (100..300 x 100..300), ramp up to y=40 over x 300..400,
    // upper plane y=40 over (400..600 x 100..300). Optional full-length wall at x=200 (y 10..30).
    private static WalkMesh Terrain(bool withWall = false)
    {
        var verts = new List<float>();
        var tris = new List<int>();
        int V(float x, float y, float z) { verts.Add(x); verts.Add(y); verts.Add(z); return verts.Count / 3 - 1; }
        void Quad(int a, int b, int c, int d) { tris.AddRange(new[] { a, b, c }); tris.AddRange(new[] { a, c, d }); }

        Quad(V(100, 10, 100), V(300, 10, 100), V(300, 10, 300), V(100, 10, 300));
        Quad(V(300, 10, 100), V(400, 40, 100), V(400, 40, 300), V(300, 10, 300));
        Quad(V(400, 40, 100), V(600, 40, 100), V(600, 40, 300), V(400, 40, 300));
        if (withWall)
        {
            Quad(V(200, 10, 100), V(200, 10, 300), V(200, 30, 300), V(200, 30, 100));
        }
        return new WalkMesh(verts.ToArray(), tris.ToArray());
    }

    [Fact]
    public void Spawn_lands_on_ground()
    {
        var r = new MeshRegion(Terrain(), new Vector2(120, 120), new Vector2(280, 280), new Vector2(200, 200), seed: 7);
        Assert.Equal(10f, r.Spawn.Y, 2);
        Assert.InRange(r.Spawn.X, 120, 280);
        Assert.InRange(r.Spawn.Z, 120, 280);
    }

    [Fact]
    public void Steps_stay_on_ground_and_in_bounds_including_the_ramp()
    {
        var mesh = Terrain();
        var r = new MeshRegion(mesh, new Vector2(120, 120), new Vector2(580, 280), new Vector2(200, 200), seed: 3);
        var p = r.Spawn;
        for (int i = 0; i < 3000; i++)
        {
            p = r.NextStep(p, stepUnits: 10f, out _);
            Assert.InRange(p.X, 120, 580);
            Assert.InRange(p.Z, 120, 280);
            Assert.True(mesh.RaycastDown(p.X, p.Z, p.Y + 1, 3, out float gy, out _), $"off the mesh at {p}");
            Assert.Equal(gy, p.Y, 2);   // Y == terrain height, not a constant
        }
    }

    [Fact]
    public void Terrain_Y_actually_varies_across_the_walk()
    {
        var r = new MeshRegion(Terrain(), new Vector2(120, 120), new Vector2(580, 280), new Vector2(390, 200), seed: 11);
        var ys = new HashSet<int>();
        var p = r.Spawn;
        for (int i = 0; i < 3000; i++)
        {
            p = r.NextStep(p, 10f, out _);
            ys.Add((int)MathF.Round(p.Y));
        }
        Assert.True(ys.Count >= 3, $"expected varied terrain heights, saw {ys.Count} distinct Y");
    }

    [Fact]
    public void Wall_is_never_crossed()
    {
        var r = new MeshRegion(Terrain(withWall: true), new Vector2(120, 120), new Vector2(280, 280),
            new Vector2(150, 200), seed: 5);
        Assert.True(r.Spawn.X < 200, "test setup: spawn must start west of the wall");
        var p = r.Spawn;
        for (int i = 0; i < 3000; i++)
        {
            p = r.NextStep(p, 10f, out _);
            Assert.True(p.X < 200f, $"crossed the wall at {p}");
        }
    }

    [Fact]
    public void Deterministic_per_seed()
    {
        MeshRegion Make(int seed) => new(Terrain(), new Vector2(120, 120), new Vector2(280, 280), new Vector2(200, 200), seed);

        var a = Make(42); var b = Make(42); var c = Make(43);
        Assert.Equal(a.Spawn, b.Spawn);   // spawn hint is walkable -> same landing for any seed

        var pa = a.Spawn; var pb = b.Spawn; var pc = c.Spawn;
        bool diverged = false;
        for (int i = 0; i < 100; i++)
        {
            pa = a.NextStep(pa, 10f, out _);
            pb = b.NextStep(pb, 10f, out _);
            pc = c.NextStep(pc, 10f, out _);
            Assert.Equal(pa, pb);
            diverged |= pa != pc;
        }
        Assert.True(diverged, "different seeds must produce different walks");
    }

    [Fact]
    public void No_walkable_ground_in_box_throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new MeshRegion(Terrain(), new Vector2(5000, 5000), new Vector2(5100, 5100), new Vector2(5050, 5050), seed: 1));
    }

    [Fact]
    public void Coordinates_never_drop_to_zero_or_below()
    {
        // mesh right up against the origin; box min at 0 must still keep X/Z >= 1
        var verts = new float[] { 0, 10, 0, 400, 10, 0, 400, 10, 400, 0, 10, 400 };
        var tris = new int[] { 0, 1, 2, 0, 2, 3 };
        var r = new MeshRegion(new WalkMesh(verts, tris), new Vector2(0, 0), new Vector2(60, 60), new Vector2(5, 5), seed: 9);
        var p = r.Spawn;
        for (int i = 0; i < 1000; i++)
        {
            p = r.NextStep(p, 10f, out _);
            Assert.True(p.X >= 1f && p.Z >= 1f, $"non-positive coordinate at {p}");
        }
    }
}
