using EqoaLoadClient.Core.Primitives;

namespace EqoaLoadClient.Core.Transport;

public static class GameMessage
{
    /// Game-channel message. `decodedPayload` is the (XOR-delta'd) record; on the wire
    /// `size` is its DECODED length and the payload itself is RLE-encoded (matches
    /// drdp_guaranteed_msg_send: type, size, seq, refnum, RLE(payload)).
    public static byte[] Encode(byte type, ushort seq, byte refnum, ReadOnlySpan<byte> decodedPayload)
    {
        var w = new PacketWriter(decodedPayload.Length + 8);
        w.WriteByte(type);
        int size = decodedPayload.Length;                       // DECODED (pre-RLE) length
        if (size < 0xFF) w.WriteByte((byte)size);
        else { w.WriteByte(0xFF); w.WriteU16LE((ushort)size); }
        w.WriteU16LE(seq);
        w.WriteByte(refnum);
        Rle.Encode(w, decodedPayload);                          // RLE-encoded payload
        return w.ToArray();
    }
}
