namespace EqoaLoadClient.Harness;

/// Staggered join schedule. A bot is not ticked (so does not join) until its offset elapses,
/// spreading logins over the ramp instead of a same-tick thundering herd of cold zone-loads.
public static class JoinSchedule
{
    /// Linear ramp: bot i joins at i * (rampMs / n). rampMs <= 0 (or n <= 1) => all join at t=0.
    public static long OffsetMs(int i, int n, int rampMs)
    {
        if (rampMs <= 0 || n <= 1)
        {
            return 0;
        }

        return (long)((double)i / n * rampMs);
    }
}
