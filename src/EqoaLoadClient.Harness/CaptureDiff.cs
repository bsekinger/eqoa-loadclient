namespace EqoaLoadClient.Harness;

public sealed record DiffResult(bool Match, int? FirstDivergenceOffset, string? Note);

public static class CaptureDiff
{
    /// P0: exact byte compare (after the caller normalizes per-session fields:
    /// InstanceID, seq counters, timestamps, CRC). Field-level decode is layered
    /// on when the first real PCSX2 capture is available.
    public static DiffResult CompareDatagram(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual)
    {
        int n = Math.Min(expected.Length, actual.Length);
        for (int i = 0; i < n; i++)
            if (expected[i] != actual[i])
                return new DiffResult(false, i, $"expected 0x{expected[i]:X2} got 0x{actual[i]:X2}");
        if (expected.Length != actual.Length)
            return new DiffResult(false, n, $"length {expected.Length} vs {actual.Length}");
        return new DiffResult(true, null, null);
    }
}
