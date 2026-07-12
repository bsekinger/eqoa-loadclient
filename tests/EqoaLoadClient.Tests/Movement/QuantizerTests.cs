using EqoaLoadClient.Core.Movement;
using EqoaLoadClient.Core.Primitives;
using Xunit;

public class QuantizerTests
{
    [Fact]
    public void PosX_zero_in_range()
    {
        var w = new PacketWriter();
        Quantizer.Write(w, 0f, -4000f, 32000f, 3);
        Assert.Equal(new byte[] { 0x1C, 0x71, 0xC7 }, w.ToArray()); // big-endian
    }

    [Fact]
    public void Max_clamps_to_all_ones()
    {
        var w = new PacketWriter();
        Quantizer.Write(w, 1000f, -1000f, 1000f, 3);
        Assert.Equal(new byte[] { 0xFF, 0xFF, 0xFF }, w.ToArray());
    }

    [Fact]
    public void Min_is_zero()
    {
        var w = new PacketWriter();
        Quantizer.Write(w, -4000f, -4000f, 32000f, 3);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x00 }, w.ToArray());
    }

    [Fact]
    public void Heading_zero_one_byte()
    {
        var w = new PacketWriter();
        Quantizer.Write(w, 0f, -MathF.PI, MathF.PI, 1);
        Assert.Equal(new byte[] { 0x80 }, w.ToArray());
    }

    [Fact]
    public void Zero_range_emits_nothing()
    {
        var w = new PacketWriter();
        Quantizer.Write(w, 5f, 0f, 0f, 1);
        Assert.Equal(0, w.Length);
    }
}
