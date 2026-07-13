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

    [Fact]
    public void Movement_byte0_is_the_world_not_an_incrementing_counter()
    {
        // Regression: the server reads byte[0] of the record as World. A counter there decodes
        // to a bogus world (1,2,3...) -> zone-change fault every packet. It must be the world id.
        var region = new BoundingBoxRegion(new Vector3(4700, 40, 16700), new Vector3(5300, 60, 17300),
            new Vector3(5000, 50, 17000), 1);
        var conn = new DrdpConnection(0x0102, 0x00010000);
        var ctx = new BotContext(conn, region, intervalMs: 100);
        ctx.State.World = 3;                                  // BotClient sets this from cfg.WorldId
        var moves = new List<byte[]>();
        ctx.OnMovementEncoded = m => moves.Add(m);

        var behavior = new MovementBehavior();
        for (long t = 0; t <= 500; t += 100) behavior.Tick(t, ctx);

        Assert.True(moves.Count >= 3);
        foreach (var m in moves)                             // every move: type,size,seq,refnum, RLE(record)
        {
            Assert.True(Rle.TryDecode(new ReadOnlySpan<byte>(m, 5, m.Length - 5), out var record, out _));
            Assert.Equal(3, record[0]);                      // world id, constant — never increments
        }
    }
}
