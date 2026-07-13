using EqoaLoadClient.Core.Primitives;

namespace EqoaLoadClient.Core.Transport;

/// Per-channel game-message run-length codec. Matches the client encoder
/// `FUN_0048f3e8` (payload writer in drdp_guaranteed_msg_send) and the emu's
/// `Run_length_decode`. Stream of blocks over runs of [nullCount zeros][litLen
/// literals], terminated by a 0x00 control byte:
///   short form (litLen &lt; 8 &amp;&amp; nullCount &lt; 16): one byte (litLen &lt;&lt; 4) | nullCount
///   long form:                                    (litLen | 0x80), then a nullCount byte
///   then `litLen` literal bytes. A 0x00 control byte ends the stream.
public static class Rle
{
    /// Encode `input` (the decoded payload) into `w`.
    public static void Encode(PacketWriter w, ReadOnlySpan<byte> input)
    {
        int pos = 0, n = input.Length;
        while (true)
        {
            int nullCount = 0;
            while (pos < n && input[pos] == 0 && nullCount < 0xFF) { pos++; nullCount++; }
            int litStart = pos, litLen = 0;
            while (pos < n && input[pos] != 0 && litLen < 0x7F) { pos++; litLen++; }

            if (litLen < 8 && nullCount < 0x10)
            {
                byte control = (byte)((litLen << 4) | nullCount);
                w.WriteByte(control);
                if (control == 0) return;               // end of stream (nulls + literals both empty)
            }
            else
            {
                w.WriteByte((byte)(litLen | 0x80));
                w.WriteByte((byte)nullCount);
            }
            for (int i = 0; i < litLen; i++) w.WriteByte(input[litStart + i]);
        }
    }

    /// Decode the RLE stream at the start of `src`. Sets `bytesConsumed` to the wire
    /// bytes used (including the 0x00 terminator). Returns false on overrun/malformed.
    public static bool TryDecode(ReadOnlySpan<byte> src, out byte[] decoded, out int bytesConsumed)
    {
        decoded = Array.Empty<byte>();
        bytesConsumed = 0;
        var outBuf = new List<byte>(64);
        int p = 0;
        while (true)
        {
            if (p >= src.Length) return false;          // no terminator
            byte c = src[p++];
            if (c == 0) { bytesConsumed = p; decoded = outBuf.ToArray(); return true; }

            int litLen, nullCount;
            if ((c & 0x80) == 0) { litLen = c >> 4; nullCount = c & 0x0F; }
            else { litLen = c & 0x7F; if (p >= src.Length) return false; nullCount = src[p++]; }

            for (int i = 0; i < nullCount; i++) outBuf.Add(0);
            if (p + litLen > src.Length) return false;
            for (int i = 0; i < litLen; i++) outBuf.Add(src[p++]);
        }
    }
}
