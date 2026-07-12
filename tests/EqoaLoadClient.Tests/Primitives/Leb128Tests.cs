using EqoaLoadClient.Core.Primitives;
using Xunit;

public class Leb128Tests
{
    [Theory]
    [InlineData(0UL, new byte[] { 0x00 })]
    [InlineData(127UL, new byte[] { 0x7F })]
    [InlineData(128UL, new byte[] { 0x80, 0x01 })]
    [InlineData(300UL, new byte[] { 0xAC, 0x02 })]
    public void Write_matches_expected(ulong value, byte[] expected)
    {
        var w = new PacketWriter();
        Leb128.Write(w, value);
        Assert.Equal(expected, w.ToArray());
    }

    [Fact]
    public void Roundtrip()
    {
        var w = new PacketWriter();
        Leb128.Write(w, 300UL);
        bool ok = Leb128.TryRead(w.AsSpan(), out ulong v, out int n);
        Assert.True(ok); Assert.Equal(300UL, v); Assert.Equal(2, n);
    }
}
