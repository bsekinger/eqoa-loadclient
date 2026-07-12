using EqoaLoadClient.Core.Primitives;

namespace EqoaLoadClient.Core.Transport;

/// Emits the bot's outbound segment. `AckState` supplies which flags/fields to add.
public static class Segment
{
    public static byte[] Build(byte flags, ushort segmentSeq, ReadOnlySpan<byte> ackFields, ReadOnlySpan<byte> messages)
    {
        var w = new PacketWriter(ackFields.Length + messages.Length + 4);
        w.WriteByte(flags);
        // flag 0x40 (echo server conn-id) is prepended by AckState into ackFields when needed.
        w.WriteU16LE(segmentSeq);
        w.WriteBytesBE(ackFields);   // pre-encoded per-flag fields (see AckState, Task 10)
        w.WriteBytesBE(messages);
        return w.ToArray();
    }
}
