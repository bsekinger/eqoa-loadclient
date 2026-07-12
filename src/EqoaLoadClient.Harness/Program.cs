using System.Net;
using System.Numerics;
using EqoaLoadClient.Core.Bot;
using EqoaLoadClient.Core.Movement;
using EqoaLoadClient.Core.Transport;
using EqoaLoadClient.Harness;

// Config-driven multi-bot runner. Settings come from App.config <appSettings>,
// overridable on the command line as key=value (e.g. `dotnet run -- BotCount=10 SpawnMode=Random`).
var cfg = FleetConfig.Load(args);
Console.WriteLine($"[fleet] {cfg}");

var serverEp = new IPEndPoint(IPAddress.Parse(cfg.Str("ServerIp", "127.0.0.1")), cfg.Int("ServerPort", 10070));
ushort opcode = cfg.Hex16("JoinOpcode", 0x0BB0);
int botCount = Math.Max(1, cfg.Int("BotCount", 1));
ushort world = cfg.UShort("WorldID", 0);
bool random = cfg.Str("SpawnMode", "Fixed").Equals("Random", StringComparison.OrdinalIgnoreCase);
int sx = cfg.Int("SpawnX", 5000), sy = cfg.Int("SpawnY", 50), sz = cfg.Int("SpawnZ", 17000);
int rangeX = cfg.Int("SpawnRangeX", 3000), rangeZ = cfg.Int("SpawnRangeZ", 3000);
int wander = cfg.Int("WanderRadius", 500);
int intervalMs = cfg.Int("IntervalMs", 100);
ushort cluster = cfg.UShort("ClusterId", 0);
int durationSec = cfg.Int("DurationSec", 0);
int seed = cfg.Int("Seed", 1);
uint baseInstance = cfg.UInt("BaseInstanceId", 0x00010000);
int baseSrcEp = cfg.Int("BaseSrcEndpoint", 0x0102);

var rng = new Random(seed);
var bots = new List<(BotClient bot, CountingChannel ch, UdpChannel inner)>(botCount);
for (int i = 0; i < botCount; i++)
{
    int bx = sx, bz = sz;
    if (random) { bx = sx + rng.Next(-rangeX, rangeX + 1); bz = sz + rng.Next(-rangeZ, rangeZ + 1); }
    var spawn = new Vector3(bx, sy, bz);

    var min = spawn - new Vector3(wander, 10, wander);
    var max = spawn + new Vector3(wander, 10, wander);
    min.X = MathF.Max(1, min.X); min.Z = MathF.Max(1, min.Z);   // stay strictly positive in X/Z
    var region = new BoundingBoxRegion(min, max, spawn, seed + i);

    var inner = new UdpChannel(serverEp);          // own socket (own UDP source port) per bot
    var ch = new CountingChannel(inner);
    var botCfg = new BotConfig
    {
        SrcEndpoint = (ushort)(baseSrcEp + i),     // unique per bot
        InstanceId = baseInstance + (uint)i,       // unique per bot (emu keys on (addr, InstanceID))
        BotIndex = (uint)i,
        WorldId = world, ClassId = 7, Level = 30, Cluster = cluster,
        JoinOpcode = opcode, IntervalMs = intervalMs, Region = region,
    };
    bots.Add((new BotClient(botCfg, ch), ch, inner));
}
Console.WriteLine($"[fleet] started {bots.Count} bot(s) {(random ? $"random within +/-({rangeX},{rangeZ}) of" : "at")} ({sx},{sy},{sz}) world {world}, " +
                  $"{(durationSec > 0 ? durationSec + "s" : "until Ctrl-C")}");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

// One shared tick loop over all bots (the fleet-scheduler pattern the Core is built for).
var sw = System.Diagnostics.Stopwatch.StartNew();
long lastReport = 0;
int tickDelayMs = Math.Max(5, intervalMs / 2);
while (!cts.IsCancellationRequested && (durationSec == 0 || sw.Elapsed.TotalSeconds < durationSec))
{
    long now = sw.ElapsedMilliseconds;
    foreach (var (bot, _, _) in bots) bot.Tick(now);

    if (sw.ElapsedMilliseconds - lastReport >= 1000)
    {
        lastReport = sw.ElapsedMilliseconds;
        int sent = bots.Sum(b => b.ch.Sent), recv = bots.Sum(b => b.ch.Received);
        int inWorld = bots.Count(b => b.bot.State == BotState.InWorld);
        Console.Write($"\r[fleet] t={sw.Elapsed:mm\\:ss} bots={bots.Count} inWorld={inWorld} sent={sent} recv={recv}   ");
    }
    try { await Task.Delay(tickDelayMs, cts.Token); } catch (OperationCanceledException) { break; }
}

Console.WriteLine("\n[fleet] shutting down — logging out all bots...");
foreach (var (bot, _, _) in bots) bot.Logout();
foreach (var (_, _, inner) in bots) inner.Dispose();
int totalSent = bots.Sum(b => b.ch.Sent), totalRecv = bots.Sum(b => b.ch.Received);
Console.WriteLine($"[fleet] done. bots={bots.Count} elapsed={sw.Elapsed:mm\\:ss} sent={totalSent} received={totalRecv}");
Console.WriteLine(totalRecv > 0
    ? "[fleet] server responded — two-way DRDP confirmed."
    : "[fleet] NO inbound (server down/crashing, or coords/wire).");

/// Wraps the real channel to count datagrams for the run summary.
sealed class CountingChannel(IUdpChannel inner) : IUdpChannel
{
    public int Sent, Received;
    public void Send(ReadOnlySpan<byte> dg) { Sent++; inner.Send(dg); }
    public bool TryReceive(out byte[] dg)
    {
        if (inner.TryReceive(out dg)) { Received++; return true; }
        return false;
    }
}
