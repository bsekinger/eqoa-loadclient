using EqoaLoadClient.Core.Primitives;
using Xunit;

public class PacketWriterTests
{
    [Fact]
    public void Writes_scalars_little_endian_and_raw_big_endian()
    {
        var w = new PacketWriter(16);
        w.WriteByte(0x12);
        w.WriteU16LE(0x3456);          // -> 56 34
        w.WriteU32LE(0x89ABCDEF);      // -> EF CD AB 89
        w.WriteBytesBE(new byte[] { 0x01, 0x02, 0x03 }); // raw, order preserved
        Assert.Equal(
            new byte[] { 0x12, 0x56, 0x34, 0xEF, 0xCD, 0xAB, 0x89, 0x01, 0x02, 0x03 },
            w.ToArray());
    }

    [Fact]
    public void Grows_past_initial_capacity()
    {
        var w = new PacketWriter(2);
        for (int i = 0; i < 100; i++) w.WriteByte((byte)i);
        Assert.Equal(100, w.Length);
        Assert.Equal(99, w.ToArray()[99]);
    }
}
