using System.Diagnostics;
using EqoaLoadClient.Core.Bot;
using EqoaLoadClient.Core.Movement;
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
    private readonly int _tagBot;                 // one bot whose position is printed every 10 s (movement proof)
    private readonly float _zoneLoadMargin;       // print when a bot comes within this many units of a cell border (0 = off)
    private readonly bool[] _nearBorder;          // per-bot band state, so zone-load prints once per approach
    private long _lastTagPrintMs = -10_000;       // tagged bot prints at ~t=0 then every 10 s

    public FleetRunner(IReadOnlyList<FleetBot> bots, int threads, int intervalMs, int durationSec,
                       int tagBot = 0, int zoneLoadMargin = 100)
    {
        _bots = bots;
        _threads = Math.Clamp(threads, 1, Math.Max(1, bots.Count));
        _tickDelayMs = Math.Max(5, intervalMs / 2);
        _durationSec = durationSec;
        _workerSweepMs = new double[_threads];
        _workerBacklog = new long[_threads];
        _tagBot = Math.Clamp(tagBot, 0, Math.Max(0, bots.Count - 1));
        _zoneLoadMargin = zoneLoadMargin;
        _nearBorder = new bool[bots.Count];
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
        // Strided partition (bot i -> thread i % _threads): each bot is ticked by exactly one thread,
        // and adjacent join offsets spread evenly across threads so no single thread carries the whole
        // ramp front (contiguous would pile bots 0..per-1 onto thread 0 during the ramp).
        while (!ct.IsCancellationRequested && (_durationSec == 0 || sw.Elapsed.TotalSeconds < _durationSec))
        {
            long now = sw.ElapsedMilliseconds;
            long t0 = Stopwatch.GetTimestamp();
            for (int i = wi; i < _bots.Count; i += _threads)
            {
                FleetBot fb = _bots[i];
                if (now >= fb.JoinAtMs)
                {
                    fb.Bot.Tick(now);
                    EmitDiagnostics(i, fb, now);   // owning thread only -> no race on Position/state
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

    /// Runs on the bot's owning tick thread right after Tick, so reads of Position/State are race-free.
    private void EmitDiagnostics(int i, FleetBot fb, long now)
    {
        var p = fb.Bot.Position;
        float d = ZoneGrid.DistToNearestBorder(p.X, p.Z, out int ncx, out int ncz);
        var (cx, cz) = ZoneGrid.CellOf(p.X, p.Z);

        // Zone-load trigger: bot just entered the load band around a cell border.
        if (_zoneLoadMargin > 0)
        {
            bool near = d <= _zoneLoadMargin;
            if (near && !_nearBorder[i])
            {
                Console.WriteLine($"[zone-load] bot {i} at ({p.X:F0},{p.Z:F0}) cell [{cx},{cz}] within {d:F0}u of a border -> server loads [{ncx},{ncz}]");
            }
            _nearBorder[i] = near;
        }

        // Tagged bot: position every 10 s (proof the bot is actually moving).
        if (i == _tagBot && now - _lastTagPrintMs >= 10_000)
        {
            _lastTagPrintMs = now;
            Console.WriteLine($"[tag] bot {i} t={now / 1000}s pos=({p.X:F0},{p.Y:F0},{p.Z:F0}) cell [{cx},{cz}] {d:F0}u to border, state={fb.Bot.State}");
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
