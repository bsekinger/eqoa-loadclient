using EqoaLoadClient.Core.Primitives;

namespace EqoaLoadClient.Core.Transport;

public static class GameMessage
{
    public static byte[] Encode(byte type, ushort seq, byte refnum, ReadOnlySpan<byte> payload)
    {
        var w = new PacketWriter(payload.Length + 6);
        w.WriteByte(type);
        if (payload.Length < 0xFF) w.WriteByte((byte)payload.Length);
        else { w.WriteByte(0xFF); w.WriteU16LE((ushort)payload.Length); }
        w.WriteU16LE(seq);
        w.WriteByte(refnum);
        w.WriteBytesBE(payload);
        return w.ToArray();
    }
}
