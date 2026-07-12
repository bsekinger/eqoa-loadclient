using System.Numerics;
using EqoaLoadClient.Core.Movement;
using EqoaLoadClient.Core.Transport;

namespace EqoaLoadClient.Core.Bot;

/// The surface a behavior may touch: the connection, its region, the clock cadence,
/// and mutable movement state. Keeps behaviors decoupled from transport internals.
public sealed class BotContext
{
    public DrdpConnection Conn { get; }
    public IMovementRegion Region { get; }
    public int IntervalMs { get; }
    public Vector3 Position { get; set; }
    public MovementState State;
    public long LastMovementMs { get; set; } = -100_000; // safe sentinel (avoids overflow on first tick)
    public Action<byte[]>? OnMovementEncoded;   // test/metrics hook

    public BotContext(DrdpConnection conn, IMovementRegion region, int intervalMs)
    { Conn = conn; Region = region; IntervalMs = intervalMs; Position = region.Spawn; State.Counter = 0; }
}
