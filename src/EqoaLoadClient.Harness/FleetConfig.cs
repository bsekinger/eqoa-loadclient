using System.Configuration;
using System.Globalization;

namespace EqoaLoadClient.Harness;

/// Reads harness settings from App.config's <appSettings>, with optional
/// command-line overrides passed as key=value (e.g. `BotCount=10 SpawnMode=Random`).
public sealed class FleetConfig
{
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

    private FleetConfig() { }

    public static FleetConfig Load(string[] args)
    {
        var cfg = new FleetConfig();
        foreach (var key in ConfigurationManager.AppSettings.AllKeys)
            if (key != null) cfg._values[key] = ConfigurationManager.AppSettings[key] ?? "";
        // command-line overrides: key=value
        foreach (var a in args)
        {
            int eq = a.IndexOf('=');
            if (eq > 0) cfg._values[a[..eq]] = a[(eq + 1)..];
        }
        return cfg;
    }

    private string Raw(string key, string fallback) =>
        _values.TryGetValue(key, out var v) && v.Length > 0 ? v : fallback;

    public string Str(string key, string fallback) => Raw(key, fallback);
    public int Int(string key, int fallback) =>
        int.TryParse(Raw(key, fallback.ToString()), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;
    public ushort UShort(string key, ushort fallback) =>
        ushort.TryParse(Raw(key, fallback.ToString()), out var v) ? v : fallback;
    public uint UInt(string key, uint fallback) =>
        uint.TryParse(Raw(key, fallback.ToString()), out var v) ? v : fallback;
    public ushort Hex16(string key, ushort fallback)
    {
        var s = Raw(key, "");
        return s.Length > 0 && ushort.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }

    public override string ToString() =>
        $"server={Str("ServerIp","?")}:{Int("ServerPort",0)} opcode=0x{Hex16("JoinOpcode",0):X4} " +
        $"bots={Int("BotCount",1)} world={UShort("WorldID",0)} mode={Str("SpawnMode","Fixed")} " +
        $"spawn=({Int("SpawnX",0)},{Int("SpawnY",0)},{Int("SpawnZ",0)}) roam={Int("RoamSpeed",100)}u/s " +
        $"interval={Int("IntervalMs",100)}ms duration={Int("DurationSec",0)}s";
}
