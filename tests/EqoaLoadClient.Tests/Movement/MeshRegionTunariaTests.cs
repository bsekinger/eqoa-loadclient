using System.Numerics;
using EqoaLoadClient.Core.Movement;
using Xunit;

/// Integration proof against the real client-derived Tunaria collision meshes. Skips (passes
/// vacuously) on machines without the extracted meshes; on William's box it walks a bot on the
/// actual Freeport-area terrain.
public class MeshRegionTunariaTests
{
    private const string MeshDir = @"C:\EQOA\Tunaria_CompleteMeshes";

    [Fact]
    public void Bot_walks_real_tunaria_terrain_at_a_hub()
    {
        if (!Directory.Exists(MeshDir)) return;   // meshes not on this machine

        var hub = new Vector2(5000, 17000);       // starting-city hub from the fleet config
        var min = hub - new Vector2(400, 400);
        var max = hub + new Vector2(400, 400);

        var mesh = WalkMesh.LoadForBounds(MeshDir, "Tunaria", min, max, margin: 100);
        Assert.NotNull(mesh);
        Assert.True(mesh!.TriCount > 1000, $"suspiciously small hub mesh: {mesh.TriCount} tris");

        var region = new MeshRegion(mesh, min, max, hub, seed: 1);
        Assert.InRange(region.Spawn.X, min.X, max.X);
        Assert.InRange(region.Spawn.Z, min.Y, max.Y);

        var ys = new HashSet<int>();
        var p = region.Spawn;
        for (int i = 0; i < 2000; i++)
        {
            p = region.NextStep(p, stepUnits: 10f, out _);
            Assert.InRange(p.X, min.X, max.X);
            Assert.InRange(p.Z, min.Y, max.Y);
            Assert.True(mesh.RaycastDown(p.X, p.Z, p.Y + 1f, 3f, out float gy, out _), $"off the mesh at {p}");
            Assert.Equal(gy, p.Y, 2);
            ys.Add((int)MathF.Round(p.Y));
        }
        Assert.True(ys.Count >= 2, "real terrain should vary in height across an 800x800 hub");
    }

    [Fact]
    public void All_five_configured_hubs_load_a_mesh_and_ground_their_spawns()
    {
        if (!Directory.Exists(MeshDir)) return;   // meshes not on this machine

        // the 5 world-0 starting-city hubs from App.config (Freeport, Qeynos, Highborne, Halas, Neriak)
        var hubs = new[]
        {
            new Vector2(25000, 15000), new Vector2(5000, 17000), new Vector2(5000, 21000),
            new Vector2(13000, 5000), new Vector2(25000, 9000),
        };
        foreach (var hub in hubs)
        {
            var mesh = WalkMesh.LoadForBounds(MeshDir, "Tunaria", hub - new Vector2(400, 400),
                hub + new Vector2(400, 400), margin: 100);
            Assert.True(mesh != null, $"hub ({hub.X},{hub.Y}): no mesh zones intersect");
            var region = new MeshRegion(mesh!, hub - new Vector2(400, 400), hub + new Vector2(400, 400), hub, seed: 1);
            Assert.True(mesh!.RaycastDown(region.Spawn.X, region.Spawn.Z, region.Spawn.Y + 1f, 3f, out float gy, out _),
                $"hub ({hub.X},{hub.Y}): spawn not on the mesh");
            Assert.Equal(gy, region.Spawn.Y, 2);
        }
    }
}
