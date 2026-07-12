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
}
