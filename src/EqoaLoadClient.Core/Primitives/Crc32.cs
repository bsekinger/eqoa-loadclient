namespace EqoaLoadClient.Core.Primitives;

public static class Crc32
{
    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var t = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[i] = c;
        }
        return t;
    }

    public static uint Compute(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (byte b in data) crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }

    /// The 4-byte datagram trailer value (written little-endian by OuterFrame).
    public const uint DrdpXor = 0xEE0E612Cu;
    public static uint Trailer(ReadOnlySpan<byte> datagramWithoutTrailer)
        => Compute(datagramWithoutTrailer) ^ DrdpXor;
}
