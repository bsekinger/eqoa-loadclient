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
        // step = speed × tick; ~100 u/s over 100 ms = 10 u/tick, a 2000-unit cell in ~20s.
        var next = ctx.Region.NextStep(prev, stepUnits: ctx.RoamSpeed * ctx.IntervalMs / 1000f, out _);
        ctx.Position = next;

        ctx.State.X = next.X; ctx.State.Y = next.Y; ctx.State.Z = next.Z;
        // Facing = movement direction in the CLIENT's yaw convention: theta = atan2(dX, dZ)
        // (0 faces +Z, +pi/2 faces +X; autofollow steer FUN_003c3480 + inbound s8*2pi/256 decode).
        // The emu re-broadcasts this byte (+128 unsigned wrap) to observers verbatim, so a wrong
        // convention here renders bots facing sideways. Hold the last facing while stationary.
        float dx = next.X - prev.X, dz = next.Z - prev.Z;
        bool moving = dx * dx + dz * dz > 1e-6f;
        if (moving)
        {
            ctx.State.Heading = MathF.Atan2(dx, dz);
        }
        ctx.State.YDelta = Math.Clamp(next.Y - prev.Y, -2000f, 2000f);
        ctx.State.AnimState = moving ? ctx.MovingAnimState : (byte)0;   // walk/run while moving, idle when held

        var w = new PacketWriter(48);
        MovementRecord.Write(w, ctx.State);
        byte[] msg = ctx.Conn.SendMovement(w.AsSpan());   // queues it for the next Flush
        ctx.OnMovementEncoded?.Invoke(msg);
    }
}
