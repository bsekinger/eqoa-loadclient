using EqoaLoadClient.Core.Transport;
using EqoaLoadClient.Harness;
using Xunit;

public class CaptureDiffTests
{
    [Fact]
    public void Identical_datagrams_report_no_divergence()
    {
        byte[] dg = OuterFrame.Build(0x0102, 0xFFFE, OuterFrame.FlagHasInstance, 0x11223344, new byte[]{1,2,3});
        var result = CaptureDiff.CompareDatagram(dg, dg);
        Assert.True(result.Match);
        Assert.Null(result.FirstDivergenceOffset);
    }

    [Fact]
    public void Divergence_reports_first_offset()
    {
        byte[] a = OuterFrame.Build(0x0102, 0xFFFE, OuterFrame.FlagHasInstance, 0x11223344, new byte[]{1,2,3});
        byte[] b = (byte[])a.Clone(); b[10] ^= 0xFF;
        var result = CaptureDiff.CompareDatagram(a, b);
        Assert.False(result.Match);
        Assert.Equal(10, result.FirstDivergenceOffset);
    }
}
