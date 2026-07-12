using EqoaLoadClient.Core.Primitives;

namespace EqoaLoadClient.Core.Transport;

public sealed class DrdpConnection
{
    private const int ResendIntervalMs = 1000, ResendSlackMs = 100;
    private const ushort WildcardDst = 0xFFFE;

    private readonly ushort _srcEp;
    private readonly uint _instanceId;
    private ushort _dstEp = WildcardDst;
    private ushort _segmentSeq = 1;              // seed 1
    private bool _identityConfirmed;             // sets NewInstance until true

    private readonly ChannelState _movement = new(0x40);
    private readonly AckState _acks = new();
    private ushort _controlSeq = 1;              // reliable control-message seq (type 0xFB); no refnum/XOR-delta

    private sealed class Pending { public byte[] Datagram = default!; public long LastSendMs; public bool Acked; }
    private readonly List<Pending> _retransmit = new();
    private byte[]? _pendingReliableMsg;         // encoded control message awaiting first flush
    private byte[]? _pendingMovementMsg;         // encoded channel-0x40 message awaiting flush (not retransmitted)

    public DrdpConnection(ushort srcEp, uint instanceId) { _srcEp = srcEp; _instanceId = instanceId; }

    public void SendReliable(ReadOnlySpan<byte> payload)
    {
        _pendingReliableMsg = ControlMessage.Encode(0xFB, _controlSeq, payload);
        _controlSeq++;
    }
    /// Queues a channel-0x40 movement message for the next flush and returns it (metrics hook).
    public byte[] SendMovement(ReadOnlySpan<byte> payload)
    { var m = _movement.EncodeNext(payload); _pendingMovementMsg = m; return m; }

    public void OnInbound(ReadOnlySpan<byte> datagram)
    {
        if (!InboundSegment.TryParse(datagram, out var p)) return;

        // Learn the server's endpoint id; identity is confirmed once we hear back.
        _dstEp = p.ServerEndpoint;
        _identityConfirmed = true;

        // (a) Acks the bot OWES the server:
        _acks.OnInboundSegmentSeq(p.SegmentSeq);
        foreach (var seq in p.ControlMessagesReceived) _acks.OnInboundControlSeq(seq);
        foreach (var (chan, seq) in p.GameMessagesReceived) _acks.NoteChannelReceived(chan, seq);

        // (b) The server's acks OF the bot's messages -> clear retransmit + advance XOR base:
        if (p.HasControlAck)
        {
            foreach (var pend in _retransmit) pend.Acked = true;   // control reliables acked up to base
        }
        if (p.ChannelReceived.TryGetValue(0x40, out var movAck)) _movement.OnPeerAckedChannelSeq(movAck);
    }

    public void NoteMovementAcked(ushort seq) => _movement.OnPeerAckedChannelSeq(seq);

    public void Flush(long nowMs, IUdpChannel ch)
    {
        // 1) retransmit due unacked reliables
        foreach (var p in _retransmit)
            if (!p.Acked && nowMs - p.LastSendMs >= ResendIntervalMs + ResendSlackMs)
            { ch.Send(p.Datagram); p.LastSendMs = nowMs; }

        // 2) build a fresh segment if we have any message or owe acks
        byte ackFlags = _acks.BuildAckFields(out byte[] ackFields);
        var msgBuf = new PacketWriter(64);
        if (_pendingReliableMsg != null) msgBuf.WriteBytesBE(_pendingReliableMsg);
        if (_pendingMovementMsg != null) msgBuf.WriteBytesBE(_pendingMovementMsg);
        bool hasMsg = msgBuf.Length > 0;
        if (!hasMsg && ackFlags == 0) return;

        byte[] segment = Segment.Build(ackFlags, _segmentSeq, ackFields, msgBuf.AsSpan());
        uint outerFlags = OuterFrame.FlagHasInstance | (_identityConfirmed ? 0 : OuterFrame.FlagNewInstance);
        byte[] dg = OuterFrame.Build(_srcEp, _dstEp, outerFlags, _instanceId, segment);
        ch.Send(dg);
        _segmentSeq++;

        // Only the reliable message is retransmitted; movement is superseded by the next tick.
        // (The establishment join is sent before movement starts, so its retransmit datagram is join-only.)
        if (_pendingReliableMsg != null)
            _retransmit.Add(new Pending { Datagram = dg, LastSendMs = nowMs });
        _pendingReliableMsg = null;
        _pendingMovementMsg = null;
    }

    public void Close(IUdpChannel ch)
    {
        // FIN = outer ResetConnection | HasInstance + InstanceID (best-effort).
        byte[] dg = OuterFrame.Build(_srcEp, _dstEp,
            OuterFrame.FlagResetConnection | OuterFrame.FlagHasInstance, _instanceId, ReadOnlySpan<byte>.Empty);
        ch.Send(dg);
    }
}
