using System.Net;
using System.Numerics;
using EqoaLoadClient.Core.Bot;
using EqoaLoadClient.Core.Movement;
using EqoaLoadClient.Core.Transport;

// usage: Harness <serverIp> <port> <joinOpcodeHex> [zoneId] [x y z]
var ip = IPAddress.Parse(args[0]);
int port = int.Parse(args[1]);
ushort opcode = Convert.ToUInt16(args[2], 16);   // LoadBotJoin is a u16 GameOpcode (emu: 0x0BB0)
ushort zone = args.Length > 3 ? ushort.Parse(args[3]) : (ushort)1;
// The emu's zone bounds-check rejects a spawn with X or Z <= 0, so default to positive coords.
var spawn = args.Length > 6 ? new Vector3(int.Parse(args[4]), int.Parse(args[5]), int.Parse(args[6])) : new Vector3(2000, 0, 2000);

var min = spawn - new Vector3(500, 10, 500);
var max = spawn + new Vector3(500, 10, 500);
min.X = MathF.Max(1, min.X); min.Z = MathF.Max(1, min.Z);   // keep the whole region strictly positive in X/Z
var region = new BoundingBoxRegion(min, max, spawn, seed: 1);
using var ch = new UdpChannel(new IPEndPoint(ip, port));
var cfg = new BotConfig
{
    SrcEndpoint = 0x0102, InstanceId = 0x00010000, BotIndex = 1,
    ZoneId = zone, ClassId = 7, Level = 30, Cluster = 0,
    JoinOpcode = opcode, IntervalMs = 100, Region = region,
};
var bot = new BotClient(cfg, ch);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
Console.WriteLine($"bot connecting to {ip}:{port} zone {zone} spawn {spawn}");
var sw = System.Diagnostics.Stopwatch.StartNew();
while (!cts.IsCancellationRequested)
{
    bot.Tick(sw.ElapsedMilliseconds);
    Console.Write($"\rstate={bot.State} t={sw.Elapsed:mm\\:ss}   ");
    await Task.Delay(50, cts.Token).ContinueWith(_ => { });
}
bot.Logout();
Console.WriteLine("\nlogged out");
