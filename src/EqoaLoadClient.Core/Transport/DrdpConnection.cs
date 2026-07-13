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
    private readonly HashSet<ushort> _recvControlOpcodes = new(); // opcodes of inbound control messages (e.g. the join reply)

    /// True once an inbound reliable control message carrying `opcode` has been seen.
    public bool ReceivedControlOpcode(ushort opcode) => _recvControlOpcodes.Contains(opcode);

    private sealed class Pending { public byte[] Datagram = default!; public long LastSendMs; public bool Acked; public ushort Seq; }
    private readonly List<Pending> _retransmit = new();
    private byte[]? _pendingReliableMsg;         // encoded control message awaiting first flush
    private ushort _pendingReliableSeq;          // control seq of _pendingReliableMsg (for retransmit reaping)
    private byte[]? _pendingMovementMsg;         // encoded channel-0x40 message awaiting flush (not retransmitted)

    public DrdpConnection(ushort srcEp, uint instanceId) { _srcEp = srcEp; _instanceId = instanceId; }

    public void SendReliable(ReadOnlySpan<byte> payload)
    {
        _pendingReliableMsg = ControlMessage.Encode(0xFB, _controlSeq, payload);
        _pendingReliableSeq = _controlSeq;
        _controlSeq++;
    }
    /// Queues a channel-0x40 movement message for the next flush and returns it (metrics hook).
    public byte[] SendMovement(ReadOnlySpan<byte> payload)
    { var m = _movement.EncodeNext(payload); _pendingMovementMsg = m; return m; }

    public void OnInbound(ReadOnlySpan<byte> datagram)
    {
        // Belt-and-suspenders: a malformed datagram is dropped, never propagates out of Tick.
        try
        {
            if (!InboundSegment.TryParse(datagram, out var p)) return;

            // Learn the server's endpoint id; identity is confirmed once we hear back.
            _dstEp = p.ServerEndpoint;
            _identityConfirmed = true;

            // (a) Acks the bot OWES the server:
            _acks.OnInboundSegmentSeq(p.SegmentSeq);
            foreach (var seq in p.ControlMessagesReceived) _acks.OnInboundControlSeq(seq);
            foreach (var (chan, seq) in p.GameMessagesReceived) _acks.NoteChannelReceived(chan, seq);
            foreach (var op in p.ControlOpcodes) _recvControlOpcodes.Add(op);

            // (b) The server's acks OF the bot's messages -> clear retransmit + advance XOR base:
            if (p.HasControlAck)
            {
                // ControlAckBase is the server's NEXT-EXPECTED control seq; seqs strictly below it are acknowledged.
                foreach (var pend in _retransmit)
                    if (pend.Seq < p.ControlAckBase) pend.Acked = true;
            }
            if (p.ChannelReceived.TryGetValue(0x40, out var movAck)) _movement.OnPeerAckedChannelSeq(movAck);
        }
        catch (IndexOutOfRangeException) { }
        catch (ArgumentOutOfRangeException) { }
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
            _retransmit.Add(new Pending { Datagram = dg, LastSendMs = nowMs, Seq = _pendingReliableSeq });
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
