using EqoaLoadClient.Harness;
using Xunit;

public class JoinScheduleTests
{
    [Fact]
    public void First_bot_joins_at_zero()
    {
        Assert.Equal(0, JoinSchedule.OffsetMs(0, 100, 90000));
    }

    [Fact]
    public void Offsets_are_monotonic_and_within_ramp()
    {
        const int n = 100, ramp = 90000;
        long prev = -1;
        for (int i = 0; i < n; i++)
        {
            long off = JoinSchedule.OffsetMs(i, n, ramp);
            Assert.True(off >= prev, $"offset regressed at i={i}");
            Assert.True(off < ramp, $"offset {off} >= ramp at i={i}");
            prev = off;
        }
    }

    [Fact]
    public void Zero_ramp_is_all_at_once()
    {
        Assert.Equal(0, JoinSchedule.OffsetMs(0, 100, 0));
        Assert.Equal(0, JoinSchedule.OffsetMs(99, 100, 0));
    }

    [Fact]
    public void Rate_scales_with_ramp_length()
    {
        // Last bot of 500 over 360s lands late in the window but before it closes.
        long last = JoinSchedule.OffsetMs(499, 500, 360000);
        Assert.InRange(last, 359000, 360000);
    }
}
