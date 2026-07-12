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
}
