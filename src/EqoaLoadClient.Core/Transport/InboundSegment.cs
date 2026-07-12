using EqoaLoadClient.Core.Primitives;

namespace EqoaLoadClient.Core.Transport;

/// Decoded server->bot datagram. Only the fields the P0 bot acts on.
public struct InboundSegment
{
    public ushort ServerEndpoint;
    public ushort SegmentSeq;
    public bool HasSegmentAck; public ushort SegmentAck;       // server acked this bot segment seq
    public bool HasControlAck; public ushort ControlAckBase;   // server acked bot control-msg seq base
    public Dictionary<byte, ushort> ChannelReceived;           // server's per-channel acks of bot game seqs
    public List<ushort> ControlMessagesReceived;               // control-msg seqs the bot must ack
    public List<(byte chan, ushort seq)> GameMessagesReceived; // game msgs the bot must ack

    public static bool TryParse(ReadOnlySpan<byte> datagram, out InboundSegment p)
    {
        p = new InboundSegment
        {
            ChannelReceived = new(), ControlMessagesReceived = new(), GameMessagesReceived = new()
        };
        if (datagram.Length < 8) return false;
        // A lying `size`/skip or a truncated datagram must yield `false`, never an exception:
        // explicit bounds checks below, plus a catch as the final backstop.
        try
        {
            var r = new PacketReader(datagram[..^4]);   // drop CRC trailer

            p.ServerEndpoint = r.ReadU16LE();
            r.ReadU16LE();                              // dst ep (ours)
            if (!r.TryReadLeb128(out ulong flagsLen)) return false;
            uint flags = (uint)(flagsLen & 0xFFFFF800);
            int bodyLen = (int)(flagsLen & 0x7FF);
            if ((flags & 0x2000) != 0) r.ReadU32LE();   // InstanceID
            if ((flags & 0x40000) != 0) r.ReadU32LE();  // data
            if ((flags & 0x8000) != 0) { r.ReadU32LE(); r.ReadU32LE(); } // addr64
            if ((flags & 0x1000) == 0) r.TryReadLeb128(out _);           // addrA (present when 0x1000 clear)
            if ((flags & 0x800) != 0) r.TryReadLeb128(out _);            // addrB
            int bodyStart = r.Pos;
            if (bodyLen > r.Remaining) return false;    // body claims more than the datagram holds

            // ---- segment ----
            byte sflags = r.ReadByte();
            if ((sflags & 0x40) != 0) r.ReadU32LE();     // echoed conn-id
            p.SegmentSeq = r.ReadU16LE();
            if ((sflags & 0x01) != 0) { p.HasSegmentAck = true; p.SegmentAck = r.ReadU16LE();
                                        if ((sflags & 0x04) != 0) r.TryReadLeb128(out _); }
            if ((sflags & 0x02) != 0) { p.HasControlAck = true; p.ControlAckBase = r.ReadU16LE();
                                        if ((sflags & 0x08) != 0) r.TryReadLeb128(out _); }
            if ((sflags & 0x10) != 0)
            {
                while (true)
                {
                    byte c = r.ReadByte();
                    if (c == 0xF8) break;
                    ushort s = r.ReadU16LE();
                    p.ChannelReceived[c] = s;
                }
            }

            // ---- messages until end of body; branch by type (control msgs carry NO refnum) ----
            int bodyEnd = bodyStart + bodyLen;
            while (r.Pos < bodyEnd)
            {
                byte type = r.ReadByte();
                int size = r.ReadByte();
                if (size == 0xFF) size = r.ReadU16LE();

                if (type < 0xF8)                          // game message: seq + refnum + payload
                {
                    ushort seq = r.ReadU16LE();
                    r.ReadByte();                         // refnum (bot doesn't reconstruct payloads for P0)
                    if (!r.CanRead(size)) return false;
                    r.Pos += size;                        // skip payload
                    p.GameMessagesReceived.Add((type, seq));
                }
                else if (type == 0xF9 || type == 0xFA || type == 0xFB)  // seq-bearing control: NO refnum
                {
                    ushort seq = r.ReadU16LE();
                    if (!r.CanRead(size)) return false;
                    r.Pos += size;                        // skip payload
                    if (type == 0xFB || type == 0xF9) p.ControlMessagesReceived.Add(seq); // 0xFA = keepalive, not acked
                }
                else if (type == 0xFC)                    // control, no seq, no refnum
                {
                    if (!r.CanRead(size)) return false;
                    r.Pos += size;                        // skip payload
                }
                else return false;                        // reserved/unknown stream type
            }
            return true;
        }
        catch (IndexOutOfRangeException) { return false; }
        catch (ArgumentOutOfRangeException) { return false; }
    }
}
