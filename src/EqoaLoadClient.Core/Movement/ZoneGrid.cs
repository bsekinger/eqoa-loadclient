namespace EqoaLoadClient.Core.Movement;

/// The server's 2000-unit zone/cell grid (`_zones[(int)(X/CellSize)][(int)(Z/CellSize)]`).
/// The server loads an adjacent zone when a character comes within ~100 units of a cell border,
/// so proximity to a border is the "about to trigger a zone load" signal.
public static class ZoneGrid
{
    public const int CellSize = 2000;

    public static (int cx, int cz) CellOf(float x, float z)
        => ((int)MathF.Floor(x / CellSize), (int)MathF.Floor(z / CellSize));

    /// Distance (world units) to the nearest X/Z cell border, and the neighbor cell across it
    /// (the zone the server would load as the character approaches).
    public static float DistToNearestBorder(float x, float z, out int neighborCx, out int neighborCz)
    {
        int cx = (int)MathF.Floor(x / CellSize), cz = (int)MathF.Floor(z / CellSize);
        float lowX = x - cx * CellSize, highX = (cx + 1) * CellSize - x;
        float lowZ = z - cz * CellSize, highZ = (cz + 1) * CellSize - z;
        float m = MathF.Min(MathF.Min(lowX, highX), MathF.Min(lowZ, highZ));
        if (m == lowX) { neighborCx = cx - 1; neighborCz = cz; }
        else if (m == highX) { neighborCx = cx + 1; neighborCz = cz; }
        else if (m == lowZ) { neighborCx = cx; neighborCz = cz - 1; }
        else { neighborCx = cx; neighborCz = cz + 1; }
        return m;
    }
}
