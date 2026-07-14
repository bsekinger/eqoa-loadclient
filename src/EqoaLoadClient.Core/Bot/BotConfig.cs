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
    public float RoamSpeed { get; init; } = 100f;  // units/sec; ~100 => a 2000-unit cell crossed in ~20s (well under a minute)
    public byte MovingAnimState { get; init; } = 0x01;  // anim byte while moving (emu enum: 0x01 walk, 0x03 run)
    public required IMovementRegion Region { get; init; }
}
