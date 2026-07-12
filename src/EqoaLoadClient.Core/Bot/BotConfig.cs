using EqoaLoadClient.Core.Movement;

namespace EqoaLoadClient.Core.Bot;

public sealed class BotConfig
{
    public ushort SrcEndpoint { get; init; }
    public uint InstanceId { get; init; }
    public uint BotIndex { get; init; }
    public ushort ZoneId { get; init; }
    public byte ClassId { get; init; }
    public byte Level { get; init; }
    public ushort Cluster { get; init; }
    public uint JoinOpcode { get; init; }          // from the emu registry
    public int IntervalMs { get; init; } = 100;
    public required IMovementRegion Region { get; init; }
}
