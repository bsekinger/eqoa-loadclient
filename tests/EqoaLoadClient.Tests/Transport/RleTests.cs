using EqoaLoadClient.Core.Primitives;
using EqoaLoadClient.Core.Transport;
using Xunit;

public class RleTests
{
    private static byte[] Enc(byte[] input)
    {
        var w = new PacketWriter();
        Rle.Encode(w, input);
        return w.ToArray();
    }

    [Fact]
    public void Literals_only_short_form_plus_terminator()
        // {1,2,3,4}: nullCount 0, litLen 4 -> control (4<<4)|0 = 0x40, literals, 0x00 end
        => Assert.Equal(new byte[] { 0x40, 1, 2, 3, 4, 0x00 }, Enc(new byte[] { 1, 2, 3, 4 }));

    [Fact]
    public void Zero_and_literal_runs()
        // {0,0x0D,0,0x04}: (null1,lit{0D}) -> 0x11 0D ; (null1,lit{04}) -> 0x11 04 ; end 0x00
        => Assert.Equal(new byte[] { 0x11, 0x0D, 0x11, 0x04, 0x00 },
            Enc(new byte[] { 0x00, 0x0D, 0x00, 0x04 }));

    [Fact]
    public void Long_null_run_uses_long_form()
        // 300 zeros -> nullCount caps 255 then 45; long form control 0x80 + nullCount byte
        => Assert.Equal(new byte[] { 0x80, 0xFF, 0x80, 0x2D, 0x00 }, Enc(new byte[300]));

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 1, 2, 3, 4 })]
    [InlineData(new byte[] { 0, 0, 0, 5, 6, 0, 0 })]
    [InlineData(new byte[] { 0, 0x0D, 0, 0x04 })]
    public void Roundtrip(byte[] input)
    {
        byte[] wire = Enc(input);
        Assert.True(Rle.TryDecode(wire, out var decoded, out int consumed));
        Assert.Equal(input, decoded);
        Assert.Equal(wire.Length, consumed);
    }

    [Fact]
    public void TryDecode_rejects_missing_terminator()
        => Assert.False(Rle.TryDecode(new byte[] { 0x40, 1, 2, 3, 4 }, out _, out _)); // literals, no 0x00
}
