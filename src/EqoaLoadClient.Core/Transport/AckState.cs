using EqoaLoadClient.Core.Primitives;

namespace EqoaLoadClient.Core.Transport;

/// Accumulates what the bot owes the server as acks, and encodes the ack fields.
public sealed class AckState
{
    private ushort _segSeq; private bool _seg;
    private ushort _ctrlSeq; private bool _ctrl;
    private readonly SortedDictionary<byte, ushort> _chan = new();

    public void OnInboundSegmentSeq(ushort seq) { _segSeq = seq; _seg = true; }
    public void OnInboundControlSeq(ushort seq) { _ctrlSeq = seq; _ctrl = true; }
    public void NoteChannelReceived(byte chan, ushort seq)
    {
        if (!_chan.TryGetValue(chan, out var cur) || seq > cur) _chan[chan] = seq;
    }
    public bool HasPending => _seg || _ctrl || _chan.Count > 0;

    /// Returns the flags byte and the pre-encoded ack fields (in header order 0x01,0x02,0x10).
    public byte BuildAckFields(out byte[] fields)
    {
        byte flags = 0;
        var w = new PacketWriter(16);
        if (_seg) { flags |= 0x01; w.WriteU16LE(_segSeq); }               // segment ack = highest received (cumulative)
        if (_ctrl) { flags |= 0x02; w.WriteU16LE((ushort)(_ctrlSeq + 1)); } // control ack = NEXT-EXPECTED (received + 1)
        if (_chan.Count > 0)
        {
            flags |= 0x10;
            foreach (var (c, s) in _chan) { w.WriteByte(c); w.WriteU16LE(s); }
            w.WriteByte(0xF8);
        }
        fields = w.ToArray();
        return flags;
    }
}
