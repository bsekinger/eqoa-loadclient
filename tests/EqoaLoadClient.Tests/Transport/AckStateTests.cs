using EqoaLoadClient.Core.Transport;
using Xunit;

public class AckStateTests
{
    [Fact]
    public void Builds_segment_and_control_acks()
    {
        var acks = new AckState();
        acks.OnInboundSegmentSeq(5);
        acks.OnInboundControlSeq(2);
        acks.NoteChannelReceived(0x00, 40);

        byte flags = acks.BuildAckFields(out byte[] fields);
        Assert.True((flags & 0x01) != 0);  // segment ack present
        Assert.True((flags & 0x02) != 0);  // control-message ack present
        Assert.True((flags & 0x10) != 0);  // per-channel list present
        // fields: [seg u16=5][ctrl u16=2][chan 0x00][seq u16=40][0xF8]
        Assert.Equal(new byte[]{0x05,0x00, 0x02,0x00, 0x00, 0x28,0x00, 0xF8}, fields);
    }
}
