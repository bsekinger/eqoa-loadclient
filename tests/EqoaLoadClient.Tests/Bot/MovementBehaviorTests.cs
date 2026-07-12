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
}
