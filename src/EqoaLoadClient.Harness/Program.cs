using System.Net;
using System.Numerics;
using EqoaLoadClient.Core.Bot;
using EqoaLoadClient.Core.Movement;
using EqoaLoadClient.Core.Transport;

// usage: Harness <serverIp> <port> <joinOpcodeHex> [zoneId] [x y z]
var ip = IPAddress.Parse(args[0]);
int port = int.Parse(args[1]);
uint opcode = Convert.ToUInt32(args[2], 16);
ushort zone = args.Length > 3 ? ushort.Parse(args[3]) : (ushort)1;
var spawn = args.Length > 6 ? new Vector3(int.Parse(args[4]), int.Parse(args[5]), int.Parse(args[6])) : new Vector3(0, 0, 0);

var region = new BoundingBoxRegion(spawn - new Vector3(500,10,500), spawn + new Vector3(500,10,500), spawn, seed: 1);
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
