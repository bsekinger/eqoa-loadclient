namespace EqoaLoadClient.Core.Primitives;

/// Growable byte buffer. Scalars are little-endian (drdp wire default);
/// quantized movement fields are written big-endian via WriteBytesBE by the caller.
public sealed class PacketWriter
{
    private byte[] _buf;
    public int Length { get; private set; }

    public PacketWriter(int capacity = 64) => _buf = new byte[Math.Max(1, capacity)];

    private void Ensure(int extra)
    {
        if (Length + extra <= _buf.Length) return;
        int n = _buf.Length * 2;
        while (n < Length + extra) n *= 2;
        Array.Resize(ref _buf, n);
    }

    public void WriteByte(byte b) { Ensure(1); _buf[Length++] = b; }

    public void WriteU16LE(ushort v) { Ensure(2); _buf[Length++] = (byte)v; _buf[Length++] = (byte)(v >> 8); }

    public void WriteU32LE(uint v)
    {
        Ensure(4);
        _buf[Length++] = (byte)v; _buf[Length++] = (byte)(v >> 8);
        _buf[Length++] = (byte)(v >> 16); _buf[Length++] = (byte)(v >> 24);
    }

    public void WriteS32LE(int v) => WriteU32LE(unchecked((uint)v));

    /// Append bytes verbatim (used for already-ordered data: big-endian quantized fields, payloads).
    public void WriteBytesBE(ReadOnlySpan<byte> src) { Ensure(src.Length); src.CopyTo(_buf.AsSpan(Length)); Length += src.Length; }

    public ReadOnlySpan<byte> AsSpan() => _buf.AsSpan(0, Length);
    public byte[] ToArray() => _buf.AsSpan(0, Length).ToArray();
    public void Reset() => Length = 0;
}
