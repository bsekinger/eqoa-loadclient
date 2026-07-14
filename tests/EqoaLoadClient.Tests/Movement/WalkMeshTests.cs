using System.Numerics;
using EqoaLoadClient.Core.Movement;
using Xunit;

public class WalkMeshTests
{
    // Synthetic world (all coords > 0, like the emu requires):
    //  - flat plane y=10 over (100..300, 100..300)
    //  - ramp y=10 -> 40 over x 300..400, z 100..300 (normal.y ~= 0.96, walkable)
    //  - full-height wall at x=200 spanning z 100..300, y 10..30
    private static WalkMesh Synthetic(bool withWall = false)
    {
        var verts = new List<float>();
        var tris = new List<int>();

        int V(float x, float y, float z) { verts.Add(x); verts.Add(y); verts.Add(z); return verts.Count / 3 - 1; }
        void Quad(int a, int b, int c, int d) { tris.AddRange(new[] { a, b, c }); tris.AddRange(new[] { a, c, d }); }

        // plane
        Quad(V(100, 10, 100), V(300, 10, 100), V(300, 10, 300), V(100, 10, 300));
        // ramp
        Quad(V(300, 10, 100), V(400, 40, 100), V(400, 40, 300), V(300, 10, 300));
        if (withWall)
        {
            Quad(V(200, 10, 100), V(200, 10, 300), V(200, 30, 300), V(200, 30, 100));
        }

        return new WalkMesh(verts.ToArray(), tris.ToArray());
    }

    [Fact]
    public void RaycastDown_hits_flat_ground()
    {
        var mesh = Synthetic();
        Assert.True(mesh.RaycastDown(150, 150, fromY: 50, maxDrop: 100, out float y, out float ny));
        Assert.Equal(10f, y, 3);
        Assert.Equal(1f, ny, 2);
    }

    [Fact]
    public void RaycastDown_hits_ramp_with_correct_height_and_normal()
    {
        var mesh = Synthetic();
        Assert.True(mesh.RaycastDown(350, 200, fromY: 100, maxDrop: 200, out float y, out float ny));
        Assert.Equal(25f, y, 1);            // halfway up the 30-unit rise
        Assert.InRange(ny, 0.9f, 1.0f);     // gentle slope, walkable
    }

    [Fact]
    public void RaycastDown_misses_outside_geometry()
    {
        var mesh = Synthetic();
        Assert.False(mesh.RaycastDown(5000, 5000, fromY: 100, maxDrop: 1000, out _, out _));
    }

    [Fact]
    public void RaycastDown_respects_maxDrop()
    {
        var mesh = Synthetic();
        // ground at 10, casting from 100 with only 20 of drop allowed -> no hit
        Assert.False(mesh.RaycastDown(150, 150, fromY: 100, maxDrop: 20, out _, out _));
    }

    [Fact]
    public void SegmentBlocked_detects_wall_crossing()
    {
        var mesh = Synthetic(withWall: true);
        Assert.True(mesh.SegmentBlocked(new Vector3(190, 15, 200), new Vector3(210, 15, 200)));
        // same crossing but above the wall top (y 30) is free
        Assert.False(mesh.SegmentBlocked(new Vector3(190, 35, 200), new Vector3(210, 35, 200)));
        // parallel to the wall, never crossing
        Assert.False(mesh.SegmentBlocked(new Vector3(190, 15, 150), new Vector3(190, 15, 250)));
    }

    [Fact]
    public void Ecol_roundtrip_and_bounds_filter()
    {
        string dir = Path.Combine(Path.GetTempPath(), "walkmesh_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // zone 0: the synthetic plane area; zone 1: far away
            WriteEcol(Path.Combine(dir, "Test_0.col"),
                new float[] { 100, 10, 100, 300, 10, 100, 300, 10, 300 }, new int[] { 0, 1, 2 });
            WriteEcol(Path.Combine(dir, "Test_1.col"),
                new float[] { 9000, 10, 9000, 9100, 10, 9000, 9100, 10, 9100 }, new int[] { 0, 1, 2 });

            var mesh = WalkMesh.LoadForBounds(dir, "Test", new Vector2(100, 100), new Vector2(400, 400), margin: 50);
            Assert.NotNull(mesh);
            Assert.Equal(1, mesh!.TriCount);                 // far zone filtered out
            Assert.True(mesh.RaycastDown(200, 150, 50, 100, out float y, out _));
            Assert.Equal(10f, y, 3);

            Assert.Null(WalkMesh.LoadForBounds(dir, "Test", new Vector2(50000, 50000), new Vector2(50100, 50100)));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// Minimal ECOL writer mirroring EQOAEmu.Collision's SaveBinary layout (we read verts/tris, skip the tree).
    private static void WriteEcol(string path, float[] verts, int[] tris)
    {
        using var w = new BinaryWriter(File.Create(path));
        w.Write(0x4C4F4345u); w.Write((ushort)1); w.Write((ushort)0);
        w.Write(verts.Length / 3);
        foreach (float f in verts) w.Write(f);
        float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
        for (int i = 0; i < verts.Length; i += 3)
        {
            minX = MathF.Min(minX, verts[i]); maxX = MathF.Max(maxX, verts[i]);
            minY = MathF.Min(minY, verts[i + 1]); maxY = MathF.Max(maxY, verts[i + 1]);
            minZ = MathF.Min(minZ, verts[i + 2]); maxZ = MathF.Max(maxZ, verts[i + 2]);
        }
        w.Write(minX); w.Write(minY); w.Write(minZ);
        w.Write(maxX); w.Write(maxY); w.Write(maxZ);
        w.Write(256);        // maxTrisPerChunk
        w.Write(0);          // nnodes (no tree; loader must not require one)
        w.Write(tris.Length / 3);
        foreach (int t in tris) w.Write(t);
    }
}
