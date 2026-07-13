using EqoaLoadClient.Core.Bot;
using EqoaLoadClient.Core.Primitives;

namespace EqoaLoadClient.Core.Movement;

public sealed class MovementBehavior : IBotBehavior
{
    public void Tick(long nowMs, BotContext ctx)
    {
        if (nowMs - ctx.LastMovementMs < ctx.IntervalMs) return;
        ctx.LastMovementMs = nowMs;

        var prev = ctx.Position;
        var next = ctx.Region.NextStep(prev, stepUnits: 30f, out float heading);
        ctx.Position = next;

        ctx.State.X = next.X; ctx.State.Y = next.Y; ctx.State.Z = next.Z;
        ctx.State.Heading = heading;
        ctx.State.YDelta = Math.Clamp(next.Y - prev.Y, -2000f, 2000f);
        ctx.State.AnimState = 0; // idle/run state; 0 is safe for P0

        var w = new PacketWriter(48);
        MovementRecord.Write(w, ctx.State);
        byte[] msg = ctx.Conn.SendMovement(w.AsSpan());   // queues it for the next Flush
        ctx.OnMovementEncoded?.Invoke(msg);
    }
}
