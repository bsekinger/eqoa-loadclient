using System.Net;
using System.Net.Sockets;

namespace EqoaLoadClient.Core.Transport;

public sealed class UdpChannel : IUdpChannel, IDisposable
{
    private readonly Socket _s;
    private readonly EndPoint _server;
    private readonly byte[] _rx = new byte[2048];

    public UdpChannel(IPEndPoint server)
    {
        _server = server;
        _s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { Blocking = false };
        _s.Connect(server);
    }

    public void Send(ReadOnlySpan<byte> dg) => _s.Send(dg);

    public bool TryReceive(out byte[] dg)
    {
        dg = Array.Empty<byte>();
        if (_s.Available <= 0) return false;
        int n = _s.Receive(_rx);
        if (n <= 0) return false;
        dg = _rx.AsSpan(0, n).ToArray();
        return true;
    }

    public void Dispose() => _s.Dispose();
}
