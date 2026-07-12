using EqoaLoadClient.Core.Primitives;

namespace EqoaLoadClient.Core.Transport;

/// Per-channel outbound seq + XOR-delta base (drdp-client-emit-contract §2).
public sealed class ChannelState
{
    public byte ChannelType { get; }
    private ushort _nextSeq = 1;      // seq seed = 1 (drdp-sequence-space-seeds)
    private ushort _ackBase = 0;      // highest seq the peer per-channel-acked
    // send history: seq -> payload (last few; base is the ackBase entry)
    private readonly Dictionary<ushort, byte[]> _history = new();

    public ChannelState(byte channelType) => ChannelType = channelType;

    public void OnPeerAckedChannelSeq(ushort seq)
    {
        if (seq > _ackBase) _ackBase = seq;
        // purge history below (seq - 0x20)
        foreach (var k in _history.Keys.Where(k => (ushort)(seq - k) > 0x20).ToList())
            _history.Remove(k);
    }

    public byte[] EncodeNext(ReadOnlySpan<byte> payload)
    {
        ushort seq = _nextSeq++;
        byte refnum = 0;
        byte[] outPayload;

        if (_history.TryGetValue(_ackBase, out var basePayload) && _ackBase != 0)
        {
            int delta = seq - _ackBase;
            if (delta > 0 && delta <= 0x20)
            {
                refnum = (byte)delta;
                outPayload = new byte[payload.Length];
                int n = Math.Min(payload.Length, basePayload.Length);
                for (int i = 0; i < n; i++) outPayload[i] = (byte)(payload[i] ^ basePayload[i]);
                for (int i = n; i < payload.Length; i++) outPayload[i] = payload[i];
            }
            else outPayload = payload.ToArray();
        }
        else outPayload = payload.ToArray();

        _history[seq] = payload.ToArray();
        return GameMessage.Encode(ChannelType, seq, refnum, outPayload);
    }
}
