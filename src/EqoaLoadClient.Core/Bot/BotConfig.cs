using EqoaLoadClient.Core.Movement;

namespace EqoaLoadClient.Core.Bot;

public sealed class BotConfig
{
    public ushort SrcEndpoint { get; init; }
    public uint InstanceId { get; init; }
    public uint BotIndex { get; init; }
    public ushort WorldId { get; init; }   // world (0=Tunaria..5=Secrets); the LoadBotJoin wire field the emu reads as "zoneId"
    public byte ClassId { get; init; }
    public byte Level { get; init; }
    public ushort Cluster { get; init; }
    public ushort JoinOpcode { get; init; }        // u16 GameOpcode from the emu registry (0x0BB0)
    public int IntervalMs { get; init; } = 100;
    public required IMovementRegion Region { get; init; }
}
