namespace EqoaLoadClient.Core.Session;

/// The bot's join sequence: DrdpConnection.SendReliable(LoadBotJoin body) on the
/// control channel; the first Flush carries NewInstance|HasInstance + InstanceID,
/// establishing the session keyed by (addr, InstanceID). No login/char-select.
public static class Establishment
{
    public const uint DefaultInstanceSeed = 0x00010000; // any stable per-bot value
}
