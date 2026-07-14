using EqoaLoadClient.Core.Movement;

namespace EqoaLoadClient.Harness;

/// Locates the per-world client-derived collision meshes (ECOL .col files exported from the
/// client .esf — see bridge finding structs/world-esf-collision-bsp.md) under a root folder laid
/// out as "<root>\<Name>_CompleteMeshes\<Name>_<zone>.col".
public static class MeshWorlds
{
    // World id 0..5 -> folder/file prefix, matching the six per-world .esf archives.
    private static readonly string[] Names = { "Tunaria", "Rathe", "Odus", "Lavastm", "Planesky", "Secrets" };

    public static string? Prefix(int worldId) =>
        worldId >= 0 && worldId < Names.Length ? Names[worldId] : null;

    /// Loads the zones intersecting the given XZ box, or null when the world has no meshes here.
    public static WalkMesh? LoadHub(string root, int worldId, float centerX, float centerZ, float halfExtent)
    {
        string? prefix = Prefix(worldId);
        if (prefix == null)
        {
            return null;
        }

        string dir = Path.Combine(root, prefix + "_CompleteMeshes");
        if (!Directory.Exists(dir))
        {
            return null;
        }

        return WalkMesh.LoadForBounds(dir, prefix,
            new System.Numerics.Vector2(centerX - halfExtent, centerZ - halfExtent),
            new System.Numerics.Vector2(centerX + halfExtent, centerZ + halfExtent));
    }
}
