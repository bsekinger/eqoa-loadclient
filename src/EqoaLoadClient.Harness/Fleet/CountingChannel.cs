using EqoaLoadClient.Core.Transport;

namespace EqoaLoadClient.Harness;

/// Wraps the real channel to count datagrams for the run summary. Each bot's channel is
/// touched by exactly one tick thread (single writer), so plain int counters are fine.
public sealed class CountingChannel(IUdpChannel inner) : IUdpChannel
{
    public int Sent, Received;

    public void Send(ReadOnlySpan<byte> dg) { Sent++; inner.Send(dg); }

    public bool TryReceive(out byte[] dg)
    {
        if (inner.TryReceive(out dg)) { Received++; return true; }
        return false;
    }
}
