using System.Numerics;
using EqoaLoadClient.Core.Bot;
using EqoaLoadClient.Core.Movement;
using EqoaLoadClient.Core.Transport;
using Xunit;

public class MovementBehaviorTests
{
    [Fact]
    public void Emits_a_movement_message_each_interval_and_stays_in_region()
    {
        var region = new BoundingBoxRegion(new Vector3(0,0,0), new Vector3(100,10,100), new Vector3(50,5,50), 7);
        var conn = new DrdpConnection(0x0102, 0xAABBCCDD);
        var ctx = new BotContext(conn, region, intervalMs: 100);
        var beh = new MovementBehavior();

        int emitted = 0;
        ctx.OnMovementEncoded = _ => emitted++;
        for (long t = 0; t <= 1000; t += 100) beh.Tick(t, ctx);
        Assert.True(emitted >= 10);            // ~one per 100ms
        Assert.True(region.Contains(ctx.Position));
    }

    [Fact]
    public void Facing_uses_client_convention_and_anim_tracks_movement()
    {
        var conn = new DrdpConnection(0x0102, 0xAABBCCDD);
        var beh = new MovementBehavior();

        // client yaw = atan2(dX, dZ): moving +X must face +pi/2, moving +Z must face 0
        var ctx = new BotContext(conn, new FixedStepRegion(new Vector3(1, 0, 0)), intervalMs: 100);
        beh.Tick(100, ctx);
        Assert.Equal(MathF.PI / 2, ctx.State.Heading, 3);
        Assert.Equal(0x01, ctx.State.AnimState);

        ctx = new BotContext(conn, new FixedStepRegion(new Vector3(0, 0, 1)), intervalMs: 100);
        ctx.MovingAnimState = 0x03;                       // run variant is passed through
        beh.Tick(100, ctx);
        Assert.Equal(0f, ctx.State.Heading, 3);
        Assert.Equal(0x03, ctx.State.AnimState);

        // held in place: idle anim, facing keeps its last value
        ctx = new BotContext(conn, new FixedStepRegion(new Vector3(1, 0, 0)), intervalMs: 100);
        beh.Tick(100, ctx);
        float facing = ctx.State.Heading;
        ((FixedStepRegion)ctx.Region).Step = Vector3.Zero;
        beh.Tick(200, ctx);
        Assert.Equal(0x00, ctx.State.AnimState);
        Assert.Equal(facing, ctx.State.Heading);
    }

    /// Test region: moves by a fixed step each call (Zero = hold position).
    private sealed class FixedStepRegion : IMovementRegion
    {
        public Vector3 Step;
        public FixedStepRegion(Vector3 step) { Step = step; }
        public Vector3 Spawn => new(100, 50, 100);
        public bool Contains(Vector3 p) => true;
        public Vector3 NextStep(Vector3 current, float stepUnits, out float heading)
        { heading = 0; return current + Step; }
    }
}
