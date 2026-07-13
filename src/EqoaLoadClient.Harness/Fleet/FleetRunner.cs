using System.Diagnostics;
using EqoaLoadClient.Core.Bot;
using EqoaLoadClient.Core.Transport;

namespace EqoaLoadClient.Harness;

/// One bot plus its transport and staggered join time.
public sealed class FleetBot
{
    public required BotClient Bot { get; init; }
    public required CountingChannel Channel { get; init; }
    public required IDisposable Inner { get; init; }
    public required long JoinAtMs { get; init; }
}

/// Drives a fleet of bots on one shared clock, sharded across N tick threads so the harness itself
/// does not become the bottleneck at high tiers. Each bot is owned by exactly one thread (the Core is
/// lock-free per-bot), and the runner reports its own sweep time so a saturated harness is visible and
/// never misread as a server stall.
public sealed class FleetRunner
{
    private readonly IReadOnlyList<FleetBot> _bots;
    private readonly int _threads;
    private readonly int _tickDelayMs;
    private readonly int _durationSec;
    private readonly double[] _workerSweepMs;
    private readonly long[] _workerBacklog;

    public FleetRunner(IReadOnlyList<FleetBot> bots, int threads, int intervalMs, int durationSec)
    {
        _bots = bots;
        _threads = Math.Clamp(threads, 1, Math.Max(1, bots.Count));
        _tickDelayMs = Math.Max(5, intervalMs / 2);
        _durationSec = durationSec;
        _workerSweepMs = new double[_threads];
        _workerBacklog = new long[_threads];
    }

    public void Run(CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var sw = Stopwatch.StartNew();

        var workers = new Thread[_threads];
        for (int w = 0; w < _threads; w++)
        {
            int wi = w;
            workers[wi] = new Thread(() => Worker(wi, sw, cts.Token)) { IsBackground = true, Name = $"fleet-tick-{wi}" };
            workers[wi].Start();
        }

        long lastReport = 0;
        while (!cts.IsCancellationRequested && (_durationSec == 0 || sw.Elapsed.TotalSeconds < _durationSec))
        {
            if (sw.ElapsedMilliseconds - lastReport >= 1000)
            {
                lastReport = sw.ElapsedMilliseconds;
                Report(sw);
            }

            Thread.Sleep(100);
        }

        // Stop the tick threads first so no worker is mid-Tick on a bot while we log it out.
        cts.Cancel();
        foreach (var t in workers)
        {
            t.Join(2000);
        }

        Console.WriteLine("\n[fleet] shutting down — logging out all bots...");
        foreach (var b in _bots)
        {
            b.Bot.Logout();
        }

        foreach (var b in _bots)
        {
            b.Inner.Dispose();
        }

        Report(sw);
        int totalRecv = 0;
        foreach (var b in _bots)
        {
            totalRecv += b.Channel.Received;
        }

        Console.WriteLine(totalRecv > 0
            ? "[fleet] server responded — two-way DRDP confirmed."
            : "[fleet] NO inbound (server down/crashing, or coords/wire).");
    }

    private void Worker(int wi, Stopwatch sw, CancellationToken ct)
    {
        // Contiguous partition of the bot list; each bot is ticked by exactly this thread.
        int per = (_bots.Count + _threads - 1) / _threads;
        int start = wi * per;
        int end = Math.Min(_bots.Count, start + per);

        while (!ct.IsCancellationRequested && (_durationSec == 0 || sw.Elapsed.TotalSeconds < _durationSec))
        {
            long now = sw.ElapsedMilliseconds;
            long t0 = Stopwatch.GetTimestamp();
            for (int i = start; i < end; i++)
            {
                FleetBot fb = _bots[i];
                if (now >= fb.JoinAtMs)
                {
                    fb.Bot.Tick(now);
                }
            }

            double sweepMs = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
            _workerSweepMs[wi] = sweepMs;
            if (sweepMs > _tickDelayMs)
            {
                _workerBacklog[wi]++;
            }

            int sleep = _tickDelayMs - (int)sweepMs;
            if (sleep > 0)
            {
                Thread.Sleep(sleep);
            }
        }
    }

    private void Report(Stopwatch sw)
    {
        int sent = 0, recv = 0, inWorld = 0, joined = 0;
        long now = sw.ElapsedMilliseconds;
        foreach (var b in _bots)
        {
            sent += b.Channel.Sent;
            recv += b.Channel.Received;
            if (b.Bot.State == BotState.InWorld)
            {
                inWorld++;
            }

            if (now >= b.JoinAtMs)
            {
                joined++;
            }
        }

        double maxSweep = 0;
        long backlog = 0;
        for (int i = 0; i < _threads; i++)
        {
            maxSweep = Math.Max(maxSweep, _workerSweepMs[i]);
            backlog += _workerBacklog[i];
        }

        double headroom = _tickDelayMs > 0 ? maxSweep / _tickDelayMs * 100.0 : 0;
        Console.WriteLine(
            $"[fleet] t={sw.Elapsed:mm\\:ss} joined={joined}/{_bots.Count} inWorld={inWorld} sent={sent} recv={recv} | " +
            $"threads={_threads} sweep(max)={maxSweep:F1}/{_tickDelayMs}ms ({headroom:F0}%) backlogTicks={backlog}");
    }
}
