using EqoaLoadClient.Core.Transport;
using Xunit;

public class GameMessageTests
{
    [Fact]
    public void First_message_seq1_refnum0_rle_payload()
    {
        var chan = new ChannelState(channelType: 0x40);
        byte[] payload = { 1, 2, 3, 4 };
        byte[] msg = chan.EncodeNext(payload);
        // type 0x40, size=DECODED 4, seq 0001 LE, refnum 0, then RLE({1,2,3,4}) = 40 01 02 03 04 00
        Assert.Equal(new byte[]{0x40, 0x04, 0x01,0x00, 0x00, 0x40, 1,2,3,4, 0x00}, msg);
    }

    [Fact]
    public void Second_message_xor_delta_then_rle_when_base_acked()
    {
        var chan = new ChannelState(0x40);
        chan.EncodeNext(new byte[]{10,20,30,40});   // seq 1
        chan.OnPeerAckedChannelSeq(1);              // ackBase now 1, history head seq 1
        byte[] msg = chan.EncodeNext(new byte[]{10,25,30,44}); // seq 2
        // refnum = 1; XOR = 00,0D,00,04; size=DECODED 4; RLE(00,0D,00,04) = 11 0D 11 04 00
        Assert.Equal(new byte[]{0x40, 0x04, 0x02,0x00, 0x01, 0x11,0x0D,0x11,0x04,0x00}, msg);
    }

    [Fact]
    public void Size_uses_ff_u16_when_large()
    {
        var chan = new ChannelState(0x40);
        byte[] big = new byte[300];
        byte[] msg = chan.EncodeNext(big);
        Assert.Equal(0x40, msg[0]);
        Assert.Equal(0xFF, msg[1]);
        Assert.Equal(300, msg[2] | msg[3] << 8); // u16 LE
    }
}
