using EqoaLoadClient.Core.Primitives;
using Xunit;

public class Crc32Tests
{
    [Fact]
    public void Standard_check_value()
        => Assert.Equal(0xCBF43926u, Crc32.Compute("123456789"u8));

    [Fact]
    public void Drdp_trailer_is_crc_xor_constant()
        => Assert.Equal(0xCBF43926u ^ 0xEE0E612Cu, Crc32.Trailer("123456789"u8));
}
