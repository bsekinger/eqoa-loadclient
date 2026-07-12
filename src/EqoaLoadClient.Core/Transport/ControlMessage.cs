using EqoaLoadClient.Core.Primitives;

namespace EqoaLoadClient.Core.Transport;

/// Reliable control-message encoder for seq-bearing control types (0xf9/0xfa/0xfb).
/// Wire layout (drdp-client-emit-contract §5, verified in drdp_control_msg_send @0x00482060):
///   type u8 + size (u8, or 0xff + u16 LE — same size codec as GameMessage) + seq u16 LE + payload.
/// Unlike GameMessage there is NO refnum byte — control traffic carries no XOR-delta.
public static class ControlMessage
{
    public static byte[] Encode(byte type, ushort seq, ReadOnlySpan<byte> payload)
    {
        var w = new PacketWriter(payload.Length + 5);
        w.WriteByte(type);
        if (payload.Length < 0xFF) w.WriteByte((byte)payload.Length);
        else { w.WriteByte(0xFF); w.WriteU16LE((ushort)payload.Length); }
        w.WriteU16LE(seq);
        w.WriteBytesBE(payload);
        return w.ToArray();
    }
}
