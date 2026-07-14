using System.Net;
using System.Numerics;
using EqoaLoadClient.Core.Bot;
using EqoaLoadClient.Core.Movement;
using EqoaLoadClient.Core.Transport;
using EqoaLoadClient.Harness;

// Config-driven fleet runner. Settings come from App.config <appSettings>, overridable on the
// command line as key=value (e.g. `dotnet run -- BotCount=200 SpawnMode=Clustered RampMs=150000`).
var cfg = FleetConfig.Load(args);
Console.WriteLine($"[fleet] {cfg}");

var serverEp = new IPEndPoint(IPAddress.Parse(cfg.Str("ServerIp", "127.0.0.1")), cfg.Int("ServerPort", 10070));
ushort opcode = cfg.Hex16("JoinOpcode", 0x0BB0);
int botCount = Math.Max(1, cfg.Int("BotCount", 1));
ushort world = cfg.UShort("WorldID", 0);
string spawnMode = cfg.Str("SpawnMode", "Clustered");
bool clustered = spawnMode.Equals("Clustered", StringComparison.OrdinalIgnoreCase);
bool random = spawnMode.Equals("Random", StringComparison.OrdinalIgnoreCase);
int sx = cfg.Int("SpawnX", 5950), sy = cfg.Int("SpawnY", 50), sz = cfg.Int("SpawnZ", 17950);
int intervalMs = cfg.Int("IntervalMs", 100);
int roamSpeed = cfg.Int("RoamSpeed", 100);
ushort cluster = cfg.UShort("ClusterId", 0);
int durationSec = cfg.Int("DurationSec", 0);
int seed = cfg.Int("Seed", 1);
uint baseInstance = cfg.UInt("BaseInstanceId", 0x00010000);
int baseSrcEp = cfg.Int("BaseSrcEndpoint", 0x0102);

// Realism dials: staggered ramp + tick-loop sharding (so the harness is not the bottleneck at high N).
int rampMs = cfg.Int("RampMs", 0);
int tickThreads = Math.Max(1, cfg.Int("TickThreads", 1));

// Hub clustering (Clustered mode): weighted hubs + in-hub wander, so cold-zone count stays bounded.
string hubSpec = cfg.Str("Hubs", "25000,15000,30; 5000,17000,25; 5000,21000,20; 13000,5000,15; 25000,9000,10");
float hubJitter = cfg.Int("HubJitter", 350);
float hubWander = cfg.Int("HubWander", 400);

// Random tail (Clustered mode): SpreadFraction of the fleet spawns at random valid cells across
// SpreadWorlds (cell-count-weighted), so the ladder covers the whole game, not just the hub cities.
double spreadFraction = Math.Clamp(cfg.Int("SpreadPercent", 25) / 100.0, 0.0, 1.0);
int[] spreadWorlds = WorldSpawns.ParseWorlds(cfg.Str("SpreadWorlds", "0,1,2,3,4,5"));

// Fixed/Random roam the world's valid area; Clustered wanders a per-hub/per-cell box instead.
IValidArea? area = null;
if (!clustered)
{
    if (!WorldBounds.TryGetValidArea(world, out area))
    {
        float fx = MathF.Max(1, sx - 6000), fz = MathF.Max(1, sz - 6000);
        area = new RectUnionArea((fx, fz, sx + 6000, sz + 6000));
        Console.WriteLine($"[fleet] world {world}: no valid-cell grid yet — roaming a fallback box around spawn.");
    }
}

IReadOnlyList<Hub> hubs = Array.Empty<Hub>();
int[] hubAssignment = Array.Empty<int>();
bool[] spreadMask = new bool[botCount];
WalkMesh?[] hubMeshes = Array.Empty<WalkMesh?>();
if (clustered)
{
    hubs = HubClustering.ParseHubs(hubSpec);
    hubAssignment = HubClustering.AssignHubs(botCount, hubs, seed);
    spreadMask = WorldSpawns.SpreadMask(botCount, spreadFraction, seed ^ 0x5bd1e995);

    // MeshRoot (e.g. C:\EQOA) upgrades hub bots to the client's walkable geometry: terrain Y,
    // wall/cliff rejection. One mesh per hub, shared read-only by its bots. Spread-tail bots and
    // worlds without extracted meshes keep the flat box regions.
    string meshRoot = cfg.Str("MeshRoot", "");
    if (meshRoot.Length > 0)
    {
        hubMeshes = new WalkMesh?[hubs.Count];
        for (int h = 0; h < hubs.Count; h++)
        {
            hubMeshes[h] = MeshWorlds.LoadHub(meshRoot, world, hubs[h].X, hubs[h].Z, hubWander);
            Console.WriteLine(hubMeshes[h] is WalkMesh m
                ? $"[fleet] hub {h} ({hubs[h].X},{hubs[h].Z}): walk mesh loaded, {m.TriCount} tris"
                : $"[fleet] hub {h} ({hubs[h].X},{hubs[h].Z}): no walk mesh — flat box region");
        }
    }
}

var perWorld = new Dictionary<int, int>();
var bots = new List<FleetBot>(botCount);
for (int i = 0; i < botCount; i++)
{
    IMovementRegion region;
    ushort botWorld = world;
    if (clustered)
    {
        if (spreadMask[i])
        {
            Spawn s = WorldSpawns.Spread(spreadWorlds, sy, hubWander, seed + 100_000 + i);
            region = s.Region;
            botWorld = (ushort)s.WorldId;
        }
        else
        {
            WalkMesh? mesh = hubMeshes.Length > 0 ? hubMeshes[hubAssignment[i]] : null;
            region = HubClustering.RegionFor(hubs[hubAssignment[i]], sy, hubJitter, hubWander, seed + i, mesh);
            botWorld = world;
        }
    }
    else
    {
        Vector3? fixedSpawn = random ? null : new Vector3(sx, sy, sz);
        region = new WorldRegion(area!, sy, seed + i, fixedSpawn);
        botWorld = world;
    }

    perWorld[botWorld] = perWorld.GetValueOrDefault(botWorld) + 1;

    var inner = new UdpChannel(serverEp);          // own socket (own UDP source port) per bot
    var ch = new CountingChannel(inner);
    var botCfg = new BotConfig
    {
        SrcEndpoint = (ushort)(baseSrcEp + i),     // unique per bot -> distinct emu session (not all "Session 0")
        InstanceId = baseInstance + (uint)i,       // unique per bot (emu keys on (addr, InstanceID))
        BotIndex = (uint)i,
        WorldId = botWorld, ClassId = 7, Level = 30, Cluster = cluster,
        JoinOpcode = opcode, IntervalMs = intervalMs, RoamSpeed = roamSpeed, Region = region,
    };
    bots.Add(new FleetBot
    {
        Bot = new BotClient(botCfg, ch),
        Channel = ch,
        Inner = inner,
        JoinAtMs = JoinSchedule.OffsetMs(i, botCount, rampMs),
    });
}

int spreadCount = 0;
for (int i = 0; i < botCount; i++)
{
    if (clustered && spreadMask[i])
    {
        spreadCount++;
    }
}

string where = clustered
    ? $"{hubs.Count} hub(s) + {spreadCount} spread across worlds [{string.Join(",", spreadWorlds)}]"
    : random ? "random valid points" : $"fixed ({sx},{sy},{sz})";
Console.WriteLine($"[fleet] {bots.Count} bot(s), {where}, ramp {rampMs}ms, {tickThreads} tick-thread(s), " +
                  $"roam {roamSpeed}u/s, {(durationSec > 0 ? durationSec + "s" : "until Ctrl-C")}");
Console.WriteLine($"[fleet] per-world spawn: {string.Join("  ", perWorld.OrderBy(kv => kv.Key).Select(kv => $"w{kv.Key}={kv.Value}"))}");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

int tagBot = cfg.Int("TagBot", 0);
int zoneLoadMargin = cfg.Int("ZoneLoadMargin", 100);
var runner = new FleetRunner(bots, tickThreads, intervalMs, durationSec, tagBot, zoneLoadMargin);
runner.Run(cts.Token);
Console.WriteLine("[fleet] done.");
