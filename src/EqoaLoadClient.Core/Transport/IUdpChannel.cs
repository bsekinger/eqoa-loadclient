namespace EqoaLoadClient.Core.Transport;
public interface IUdpChannel
{
    void Send(ReadOnlySpan<byte> datagram);
    bool TryReceive(out byte[] datagram);
}
