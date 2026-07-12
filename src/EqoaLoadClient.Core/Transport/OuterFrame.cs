using EqoaLoadClient.Core.Primitives;

namespace EqoaLoadClient.Core.Transport;

public static class OuterFrame
{
    public const uint FlagHasInstance = 0x2000;
    public const uint FlagNewInstance = 0x80000;
    public const uint FlagResetConnection = 0x10000;
    public const uint FlagNoAddrA = 0x1000;   // SET => addrA varint absent (receiver uses UDP source addr)

    /// One message per datagram (the bot's pattern). Minimal header: HasInstance
    /// (+ NewInstance during establishment) and FlagNoAddrA so no address fields
    /// ride the wire — the emu keys the peer off the UDP source. Setting 0x1000 is
    /// REQUIRED to omit addrA (the finding's gate is inverted); without it a
    /// conformant parser would read an addrA varint that isn't there. `instanceId`
    /// is emitted when HasInstance is set.
    public static byte[] Build(ushort srcEp, ushort dstEp, uint flags, uint instanceId, ReadOnlySpan<byte> body)
    {
        if (body.Length > 0x7FF) throw new ArgumentException("body too long for one message");
        uint hdrFlags = flags | FlagNoAddrA;                 // suppress addrA; no addrB/addr64
        var w = new PacketWriter(body.Length + 16);
        w.WriteU16LE(srcEp);
        w.WriteU16LE(dstEp);
        Leb128.Write(w, hdrFlags | (uint)body.Length);       // flags_len
        if ((hdrFlags & FlagHasInstance) != 0) w.WriteU32LE(instanceId);
        w.WriteBytesBE(body);
        uint trailer = Crc32.Trailer(w.AsSpan());
        w.WriteU32LE(trailer);
        return w.ToArray();
    }
}
