using System.Numerics;
using EqoaLoadClient.Core.Bot;
using EqoaLoadClient.Core.Movement;
using EqoaLoadClient.Core.Primitives;
using EqoaLoadClient.Core.Transport;
using Xunit;

public class BotClientTests
{
    private sealed class FakeChannel : IUdpChannel
    {
        public List<byte[]> Sent = new();
        public Queue<byte[]> Inbound = new();
        public void Send(ReadOnlySpan<byte> dg) => Sent.Add(dg.ToArray());
        public bool TryReceive(out byte[] dg)
        {
            if (Inbound.Count > 0) { dg = Inbound.Dequeue(); return true; }
            dg = Array.Empty<byte>(); return false;
        }
    }

    // A server LoadBotJoin reply: a 0xfb control message whose payload is {u16 opcode, u32 entityId}.
    private static byte[] BuildJoinReply(ushort opcode, uint entityId)
    {
        var seg = new PacketWriter();
        seg.WriteByte(0x00);                       // segment flags: none
        seg.WriteU16LE(1);                         // segment seq
        seg.WriteByte(0xFB); seg.WriteByte(6); seg.WriteU16LE(1);  // control: type,size=6,seq=1 (no refnum)
        seg.WriteU16LE(opcode); seg.WriteU32LE(entityId);          // payload {opcode, entityId}
        return OuterFrame.Build(0x2001, 0x0102, OuterFrame.FlagHasInstance, 0x1122, seg.AsSpan());
    }

    [Fact]
    public void Waits_for_join_reply_before_moving()
    {
        const ushort opcode = 0x0BB0;
        var cfg = new BotConfig
        {
            SrcEndpoint = 0x0102, InstanceId = 0x00010000, BotIndex = 1,
            WorldId = 0, ClassId = 7, Level = 30, Cluster = 0,
            JoinOpcode = opcode, IntervalMs = 100,
            Region = new BoundingBoxRegion(new Vector3(4000,0,4000), new Vector3(6000,10,6000), new Vector3(5000,5,5000), 1),
        };
        var ch = new FakeChannel();
        var bot = new BotClient(cfg, ch);

        bot.Tick(0);                                   // sends the join
        Assert.NotEmpty(ch.Sent);
        Assert.Equal(BotState.Joining, bot.State);     // NOT InWorld until the reply arrives

        bot.Tick(100);
        Assert.Equal(BotState.Joining, bot.State);     // still waiting

        ch.Inbound.Enqueue(BuildJoinReply(opcode, 0x12345678));
        bot.Tick(200);
        Assert.Equal(BotState.InWorld, bot.State);     // reply seen -> InWorld

        int before = ch.Sent.Count;
        for (long t = 300; t <= 1200; t += 100) bot.Tick(t);
        Assert.True(ch.Sent.Count > before);           // movement datagrams flowed

        bot.Logout();
        Assert.Equal(BotState.Closed, bot.State);
    }
}
