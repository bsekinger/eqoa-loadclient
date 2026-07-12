using System.Net;
using System.Numerics;
using EqoaLoadClient.Core.Bot;
using EqoaLoadClient.Core.Movement;
using EqoaLoadClient.Core.Transport;

// usage: Harness <serverIp> <port> <joinOpcodeHex> [zoneId] [x y z] [durationSec]
var ip = IPAddress.Parse(args[0]);
int port = int.Parse(args[1]);
ushort opcode = Convert.ToUInt16(args[2], 16);   // LoadBotJoin is a u16 GameOpcode (emu: 0x0BB0)
ushort zone = args.Length > 3 ? ushort.Parse(args[3]) : (ushort)0;
// The emu's zone bounds-check rejects a spawn with X or Z <= 0, so default to positive coords.
var spawn = args.Length > 6 ? new Vector3(int.Parse(args[4]), int.Parse(args[5]), int.Parse(args[6])) : new Vector3(2000, 0, 2000);
int durationSec = args.Length > 7 ? int.Parse(args[7]) : 0;   // 0 = run until Ctrl-C

var min = spawn - new Vector3(500, 10, 500);
var max = spawn + new Vector3(500, 10, 500);
min.X = MathF.Max(1, min.X); min.Z = MathF.Max(1, min.Z);   // keep the whole region strictly positive in X/Z
var region = new BoundingBoxRegion(min, max, spawn, seed: 1);

var inner = new UdpChannel(new IPEndPoint(ip, port));
var ch = new CountingChannel(inner);
var cfg = new BotConfig
{
    SrcEndpoint = 0x0102, InstanceId = 0x00010000, BotIndex = 1,
    ZoneId = zone, ClassId = 7, Level = 30, Cluster = 0,
    JoinOpcode = opcode, IntervalMs = 100, Region = region,
};
var bot = new BotClient(cfg, ch);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
Console.WriteLine($"bot -> {ip}:{port} zone {zone} spawn {spawn} opcode 0x{opcode:X4} " +
                  $"duration {(durationSec > 0 ? durationSec + "s" : "until Ctrl-C")}");
var sw = System.Diagnostics.Stopwatch.StartNew();
long lastRecvReport = 0;
while (!cts.IsCancellationRequested && (durationSec == 0 || sw.Elapsed.TotalSeconds < durationSec))
{
    bot.Tick(sw.ElapsedMilliseconds);
    if (ch.Received != lastRecvReport)   // note first/each server response on its own line
    {
        Console.WriteLine($"\n[{sw.Elapsed:mm\\:ss}] inbound from server: total {ch.Received}");
        lastRecvReport = ch.Received;
    }
    Console.Write($"\rstate={bot.State} t={sw.Elapsed:mm\\:ss} sent={ch.Sent} recv={ch.Received}   ");
    try { await Task.Delay(50, cts.Token); } catch (OperationCanceledException) { break; }
}
bot.Logout();
Console.WriteLine($"\n--- smoke summary ---");
Console.WriteLine($"final state : {bot.State}");
Console.WriteLine($"elapsed     : {sw.Elapsed:mm\\:ss}");
Console.WriteLine($"datagrams   : sent {ch.Sent}, received {ch.Received}");
Console.WriteLine(ch.Received > 0
    ? "RESULT: server responded — two-way DRDP confirmed (join parsed, server replied)."
    : "RESULT: NO inbound from server (check reachability / CRC / framing / opcode / spawn coords).");
inner.Dispose();

/// Wraps the real channel to count datagrams for the smoke run.
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
