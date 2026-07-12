using EqoaLoadClient.Core.Primitives;
using EqoaLoadClient.Core.Transport;
using Xunit;

public class InboundSegmentTests
{
    // Build a server->bot datagram: endpoints, per-msg header (HasInstance),
    // segment {flags=0x01|0x10, segSeq=9, segAck=3, chan 0x00 seq 0x28 + 0xF8},
    // then one control message type 0xFB seq 4.
    private static byte[] BuildServerDatagram()
    {
        var seg = new PacketWriter();
        seg.WriteByte(0x01 | 0x10);      // flags: seg-ack + per-channel list
        seg.WriteU16LE(9);               // segment seq
        seg.WriteU16LE(3);               // flag 0x01: highest bot segment seq the server acked
        seg.WriteByte(0x00); seg.WriteU16LE(0x28); seg.WriteByte(0xF8); // chan 0x00 recv seq 40
        // one control message: type 0xFB, size 1, seq 4, refnum 0, payload {0xAA}
        seg.WriteByte(0xFB); seg.WriteByte(1); seg.WriteU16LE(4); seg.WriteByte(0); seg.WriteByte(0xAA);
        return OuterFrame.Build(srcEp: 0x2001, dstEp: 0x0102,
            flags: OuterFrame.FlagHasInstance, instanceId: 0x1122, body: seg.AsSpan());
    }

    [Fact]
    public void Parses_server_ack_and_received_messages()
    {
        Assert.True(InboundSegment.TryParse(BuildServerDatagram(), out var p));
        Assert.Equal((ushort)0x2001, p.ServerEndpoint);
        Assert.Equal((ushort)9, p.SegmentSeq);
        Assert.True(p.HasSegmentAck);
        Assert.Equal((ushort)3, p.SegmentAck);                 // server acked bot seg seq 3
        Assert.Contains((byte)0x00, p.ChannelReceived.Keys);   // will be ignored by bot; server's recv of chan 0
        Assert.Single(p.ControlMessagesReceived);
        Assert.Equal((ushort)4, p.ControlMessagesReceived[0]); // bot must ack control seq 4
    }
}
