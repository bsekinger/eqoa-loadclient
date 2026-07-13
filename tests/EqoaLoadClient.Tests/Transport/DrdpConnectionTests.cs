using System.Linq;
using EqoaLoadClient.Core.Primitives;
using EqoaLoadClient.Core.Transport;
using Xunit;

public class DrdpConnectionTests
{
    private sealed class FakeChannel : IUdpChannel
    {
        public List<byte[]> Sent = new();
        public void Send(ReadOnlySpan<byte> dg) => Sent.Add(dg.ToArray());
        public bool TryReceive(out byte[] dg) { dg = Array.Empty<byte>(); return false; }
    }

    // Minimal server->bot datagram carrying a flag-0x02 control ack (next-expected control seq = controlBase).
    private static byte[] BuildControlAck(ushort segSeq, ushort controlBase)
    {
        var seg = new PacketWriter();
        seg.WriteByte(0x01 | 0x02);         // seg-ack + control-message ack
        seg.WriteU16LE(segSeq);             // server segment seq
        seg.WriteU16LE(0);                  // flag 0x01: server's ack of bot seg seq (irrelevant here)
        seg.WriteU16LE(controlBase);        // flag 0x02: server's NEXT-EXPECTED control seq
        return OuterFrame.Build(srcEp: 0x2001, dstEp: 0x0102,
            flags: OuterFrame.FlagHasInstance, instanceId: 0x1122, body: seg.AsSpan());
    }

    [Fact]
    public void First_datagram_has_new_instance_and_segment_seq_1()
    {
        var conn = new DrdpConnection(srcEp: 0x0102, instanceId: 0xAABBCCDD);
        var ch = new FakeChannel();
        conn.SendReliable(new byte[]{ 0x01 }); // e.g. a LoadBotJoin body
        conn.Flush(nowMs: 0, ch);
        Assert.Single(ch.Sent);
        var dg = ch.Sent[0];
        // src ep LE
        Assert.Equal(new byte[]{0x02,0x01}, dg[0..2]);
        // flags_len contains NewInstance|HasInstance -> body wrapped; InstanceID present
        Assert.Contains<byte>(0xDD, dg); // InstanceID low byte appears
    }

    [Fact]
    public void Unacked_reliable_is_retransmitted_after_interval()
    {
        var conn = new DrdpConnection(0x0102, 0xAABBCCDD);
        var ch = new FakeChannel();
        conn.SendReliable(new byte[]{ 0x01 });
        conn.Flush(0, ch);                 // send #1
        conn.Flush(500, ch);               // too soon, no resend
        conn.Flush(1200, ch);              // >= 0 + 1000 + 100 -> resend
        Assert.Equal(2, ch.Sent.Count);
    }

    [Fact]
    public void Control_ack_clears_only_retransmits_below_base()
    {
        var conn = new DrdpConnection(0x0102, 0xAABBCCDD);
        var ch = new FakeChannel();

        conn.SendReliable(new byte[] { 0x01 }); conn.Flush(0, ch);   // control seq 1
        conn.SendReliable(new byte[] { 0x02 }); conn.Flush(0, ch);   // control seq 2
        byte[] dg1 = ch.Sent[0], dg2 = ch.Sent[1];

        // Server next-expected control seq = 2 => seqs strictly < 2 acked => only seq 1.
        conn.OnInbound(BuildControlAck(segSeq: 5, controlBase: 2));

        int before = ch.Sent.Count;
        conn.Flush(1200, ch);                                        // past resend interval
        var resends = ch.Sent.Skip(before).ToList();

        Assert.Contains(resends, x => x.SequenceEqual(dg2));        // seq 2 unacked -> retransmitted
        Assert.DoesNotContain(resends, x => x.SequenceEqual(dg1));  // seq 1 acked -> not retransmitted
    }

    // G1: end-to-end. A server datagram carrying a segment (flag 0x01 seg-ack), a game-channel
    // message, and a corrected 0xFB control message drives the bot's next flush to emit
    // flag 0x02 = received-control-seq + 1 and flag 0x01 = received segment seq.
    [Fact]
    public void Flush_emits_next_expected_control_ack_and_segment_ack_after_inbound()
    {
        var seg = new PacketWriter();
        seg.WriteByte(0x01);                 // server seg flags: seg-ack
        seg.WriteU16LE(9);                   // server segment seq (bot will echo as flag 0x01)
        seg.WriteU16LE(3);                   // flag 0x01: server's ack of bot seg seq (irrelevant here)
        // game message on channel 0x00 (bot owes a flag-0x10 ack): type,size(decoded),seq,refnum,RLE-payload
        seg.WriteByte(0x00); seg.WriteByte(1); seg.WriteU16LE(0x28); seg.WriteByte(0); // type,size=1,seq,refnum
        seg.WriteByte(0x10); seg.WriteByte(0x11); seg.WriteByte(0x00);                 // RLE({0x11}) = 10 11 00
        // control message (corrected format, NO refnum): type 0xFB, size 2, seq 7, payload {0xAA,0xBB}
        seg.WriteByte(0xFB); seg.WriteByte(2); seg.WriteU16LE(7); seg.WriteByte(0xAA); seg.WriteByte(0xBB);
        byte[] serverDg = OuterFrame.Build(srcEp: 0x2001, dstEp: 0x0102,
            flags: OuterFrame.FlagHasInstance, instanceId: 0x1122, body: seg.AsSpan());

        var conn = new DrdpConnection(srcEp: 0x0102, instanceId: 0xAABBCCDD);
        var ch = new FakeChannel();
        conn.OnInbound(serverDg);
        conn.Flush(nowMs: 100, ch);

        Assert.Single(ch.Sent);              // one pure-ack datagram
        Assert.True(InboundSegment.TryParse(ch.Sent[0], out var op));   // wire form is symmetric; reuse the parser
        Assert.True(op.HasSegmentAck);
        Assert.Equal((ushort)9, op.SegmentAck);       // flag 0x01 = received segment seq
        Assert.True(op.HasControlAck);
        Assert.Equal((ushort)8, op.ControlAckBase);   // flag 0x02 = received control seq (7) + 1
        Assert.Equal((ushort)0x28, op.ChannelReceived[0x00]);  // flag 0x10 = received game-channel seq
    }
}
