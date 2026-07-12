using System.Numerics;
using EqoaLoadClient.Core.Bot;
using EqoaLoadClient.Core.Movement;
using EqoaLoadClient.Core.Transport;
using Xunit;

public class BotClientTests
{
    private sealed class FakeChannel : IUdpChannel
    {
        public List<byte[]> Sent = new();
        public void Send(ReadOnlySpan<byte> dg) => Sent.Add(dg.ToArray());
        public bool TryReceive(out byte[] dg) { dg = Array.Empty<byte>(); return false; }
    }

    [Fact]
    public void Join_then_movement_then_logout()
    {
        var cfg = new BotConfig
        {
            SrcEndpoint = 0x0102, InstanceId = 0x00010000, BotIndex = 1,
            WorldId = 1, ClassId = 7, Level = 30, Cluster = 0,
            JoinOpcode = 0x00000901, IntervalMs = 100,
            Region = new BoundingBoxRegion(new Vector3(0,0,0), new Vector3(100,10,100), new Vector3(50,5,50), 1),
        };
        var ch = new FakeChannel();
        var bot = new BotClient(cfg, ch);

        bot.Tick(0);          // establishment: sends join datagram
        Assert.NotEmpty(ch.Sent);
        Assert.Equal(BotState.InWorld, bot.State);

        int before = ch.Sent.Count;
        for (long t = 100; t <= 1000; t += 100) bot.Tick(t);
        Assert.True(ch.Sent.Count > before);   // movement datagrams flowed

        bot.Logout();
        Assert.Equal(BotState.Closed, bot.State);
    }
}
