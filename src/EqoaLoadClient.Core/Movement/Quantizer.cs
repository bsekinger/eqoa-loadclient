using EqoaLoadClient.Core.Primitives;

namespace EqoaLoadClient.Core.Movement;

public static class Quantizer
{
    /// Writes `nbytes` big-endian, or nothing when max==min (zero-range component).
    public static void Write(PacketWriter w, float value, float min, float max, int nbytes)
    {
        float span = max - min;
        if (span == 0f) return; // omitted, matches FUN_012bb048 guard

        long levels = 1L << (8 * nbytes);
        float frac = (value - min) / span;
        if (frac < 0f) frac = 0f; else if (frac > 1f) frac = 1f;
        long q = (long)MathF.Round(frac * levels, MidpointRounding.AwayFromZero);
        if (q > levels - 1) q = levels - 1;

        for (int i = nbytes - 1; i >= 0; i--)      // MSB first
            w.WriteByte((byte)((q >> (i * 8)) & 0xFF));
    }
}
