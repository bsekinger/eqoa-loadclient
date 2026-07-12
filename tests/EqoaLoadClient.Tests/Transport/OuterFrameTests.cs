using EqoaLoadClient.Core.Primitives;
using EqoaLoadClient.Core.Transport;
using Xunit;

public class OuterFrameTests
{
    [Fact]
    public void Wraps_body_with_endpoints_instance_and_crc()
    {
        byte[] body = { 0xDE, 0xAD };
        var dg = OuterFrame.Build(srcEp: 0x0102, dstEp: 0xFFFE,
            flags: 0x2000, instanceId: 0x11223344, body: body);

        // [0..1] src LE, [2..3] dst LE
        Assert.Equal(new byte[]{0x02,0x01,0xFE,0xFF}, dg[0..4]);
        // flags_len = (HasInstance 0x2000 | NoAddrA 0x1000) | len(2) = 0x3002 -> LEB128 82 60
        Assert.Equal(new byte[]{0x82,0x60}, dg[4..6]);
        // InstanceID LE
        Assert.Equal(new byte[]{0x44,0x33,0x22,0x11}, dg[6..10]);
        // body
        Assert.Equal(body, dg[10..12]);
        // trailing 4-byte CRC LE over dg[0..^4]
        uint expect = Crc32.Trailer(dg.AsSpan(0, dg.Length - 4));
        uint got = (uint)(dg[^4] | dg[^3] << 8 | dg[^2] << 16 | dg[^1] << 24);
        Assert.Equal(expect, got);
        Assert.Equal(16, dg.Length); // 4 + 2 + 4 + 2 + 4
    }
}
