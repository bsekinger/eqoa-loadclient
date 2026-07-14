using EqoaLoadClient.Core.Movement;
using EqoaLoadClient.Core.Session;
using EqoaLoadClient.Core.Transport;

namespace EqoaLoadClient.Core.Bot;

public sealed class BotClient
{
    private readonly BotConfig _cfg;
    private readonly IUdpChannel _ch;
    private readonly DrdpConnection _conn;
    private readonly BotContext _ctx;
    private readonly IBotBehavior _movement = new MovementBehavior();

    public BotState State { get; private set; } = BotState.New;

    /// Current wander position (updated by MovementBehavior each move). Read on the bot's owning
    /// thread for diagnostics — the fleet ticks each bot from exactly one thread.
    public System.Numerics.Vector3 Position => _ctx.Position;

    public BotClient(BotConfig cfg, IUdpChannel ch)
    {
        _cfg = cfg; _ch = ch;
        _conn = new DrdpConnection(cfg.SrcEndpoint, cfg.InstanceId);
        _ctx = new BotContext(_conn, cfg.Region, cfg.IntervalMs);
        _ctx.State.World = (byte)cfg.WorldId;   // byte[0] of every movement record = the bot's world (0 = Tunaria)
        _ctx.RoamSpeed = cfg.RoamSpeed;
        _ctx.MovingAnimState = cfg.MovingAnimState;
    }

    /// One unit of work. The fleet (or RunAsync) calls this on its clock.
    public void Tick(long nowMs)
    {
        // drain inbound
        while (_ch.TryReceive(out var dg)) _conn.OnInbound(dg);

        switch (State)
        {
            case BotState.New:
                var spawn = _cfg.Region.Spawn;
                byte[] join = LoadBotJoin.Encode(_cfg.JoinOpcode, _cfg.BotIndex, _cfg.WorldId,
                    (int)spawn.X, (int)spawn.Y, (int)spawn.Z, _cfg.ClassId, _cfg.Level, _cfg.Cluster);
                _conn.SendReliable(join);
                _conn.Flush(nowMs, _ch);
                State = BotState.Joining;
                break;

            case BotState.Joining:
                // Keep flushing (retransmit the join + ack the server's reply); do NOT send movement
                // yet — the server NREs if channel-0x40 movement arrives before the character exists.
                // Advance to InWorld only once the server echoes the join opcode (its LoadBotJoin reply).
                _conn.Flush(nowMs, _ch);
                if (_conn.ReceivedControlOpcode(_cfg.JoinOpcode)) State = BotState.InWorld;
                break;

            case BotState.InWorld:
                _movement.Tick(nowMs, _ctx);
                _conn.Flush(nowMs, _ch);
                break;
        }
    }

    public void Logout()
    {
        _conn.Close(_ch);
        State = BotState.Closed;
    }

    /// Convenience self-driving loop for standalone/harness use.
    public async Task RunAsync(CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!ct.IsCancellationRequested)
        {
            Tick(sw.ElapsedMilliseconds);
            try { await Task.Delay(Math.Max(5, _cfg.IntervalMs / 2), ct); } catch (OperationCanceledException) { break; }
        }
        Logout();
    }
}
