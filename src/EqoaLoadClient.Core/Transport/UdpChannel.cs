using System.Net;
using System.Net.Sockets;

namespace EqoaLoadClient.Core.Transport;

public sealed class UdpChannel : IUdpChannel, IDisposable
{
    private readonly Socket _s;
    private readonly EndPoint _server;
    private readonly byte[] _rx = new byte[2048];

    // SIO_UDP_CONNRESET: stop Windows from raising WSAECONNRESET on the socket when a
    // prior send drew an ICMP port-unreachable (server briefly down). Otherwise a
    // transient server restart would kill the bot.
    private const int SioUdpConnReset = -1744830452; // 0x9800000C

    public UdpChannel(IPEndPoint server)
    {
        _server = server;
        _s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { Blocking = false };
        if (OperatingSystem.IsWindows())
        {
            try { _s.IOControl(SioUdpConnReset, new byte[] { 0, 0, 0, 0 }, null); } catch { /* non-Windows or unsupported */ }
        }
        _s.Connect(server);
    }

    public void Send(ReadOnlySpan<byte> dg)
    {
        try { _s.Send(dg); }
        catch (SocketException) { /* transient (e.g. server down); the tick loop keeps going */ }
    }

    public bool TryReceive(out byte[] dg)
    {
        dg = Array.Empty<byte>();
        try
        {
            if (_s.Available <= 0) return false;
            int n = _s.Receive(_rx);
            if (n <= 0) return false;
            dg = _rx.AsSpan(0, n).ToArray();
            return true;
        }
        catch (SocketException) { return false; }
    }

    public void Dispose() => _s.Dispose();
}
