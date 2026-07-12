using EqoaLoadClient.Core.Transport;
using Xunit;

public class ControlMessageTests
{
    [Fact]
    public void Encodes_type_size_seq_payload_with_no_refnum()
    {
        // type 0xFB, seq 1, payload {0xAA,0xBB} -> FB 02 01 00 AA BB
        byte[] msg = ControlMessage.Encode(0xFB, seq: 1, payload: new byte[] { 0xAA, 0xBB });
        Assert.Equal(new byte[] { 0xFB, 0x02, 0x01, 0x00, 0xAA, 0xBB }, msg);
    }

    [Fact]
    public void Empty_payload_is_type_size0_seq_only()
    {
        byte[] msg = ControlMessage.Encode(0xFB, seq: 7, payload: ReadOnlySpan<byte>.Empty);
        Assert.Equal(new byte[] { 0xFB, 0x00, 0x07, 0x00 }, msg);
    }

    [Fact]
    public void Size_uses_ff_u16_when_large()
    {
        byte[] big = new byte[300];
        byte[] msg = ControlMessage.Encode(0xFB, seq: 2, payload: big);
        Assert.Equal(0xFB, msg[0]);
        Assert.Equal(0xFF, msg[1]);
        Assert.Equal(300, msg[2] | msg[3] << 8);   // u16 LE size
        Assert.Equal(2, msg[4] | msg[5] << 8);      // u16 LE seq immediately after size (no refnum)
    }
}
