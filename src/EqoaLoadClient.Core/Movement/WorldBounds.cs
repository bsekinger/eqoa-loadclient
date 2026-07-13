namespace EqoaLoadClient.Core.Movement;

/// Per-world valid roaming area. Tunaria (world 0) is seeded from EQOAEmu WorldLayer.StartZones'
/// two all-land blocks (relayed via the bridge). Worlds 1..5 await the full StartZones grid dump
/// (bridge request 2026-07-12-per-world-valid-cell-grid); until then callers fall back to a box.
///
/// When the grid arrives, replace the RectUnionArea seeds with CellGridArea(validCells) per world.
public static class WorldBounds
{
    // Tunaria all-land blocks (world units): block A + block B. Their union is an L-shape covering
    // the bulk of playable Tunaria; every cell inside is a real land zone (server-confirmed).
    private static readonly IValidArea Tunaria = new RectUnionArea(
        (4000f, 4000f, 12000f, 22000f),     // block A: X[4000,12000] Z[4000,22000]
        (12000f, 4000f, 20000f, 32000f));   // block B: X[12000,20000] Z[4000,32000]

    /// True if we have an authoritative valid area for this world. False => caller should fall back.
    public static bool TryGetValidArea(int worldId, out IValidArea area)
    {
        switch (worldId)
        {
            case 0: area = Tunaria; return true;
            default: area = Tunaria; return false;   // no grid yet; `area` is a harmless placeholder
        }
    }
}
