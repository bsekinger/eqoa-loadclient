namespace EqoaLoadClient.Core.Primitives;

public ref struct PacketReader
{
    private readonly ReadOnlySpan<byte> _b;
    public int Pos;
    public PacketReader(ReadOnlySpan<byte> b) { _b = b; Pos = 0; }
    public bool AtEnd => Pos >= _b.Length;
    public byte ReadByte() => _b[Pos++];
    public ushort ReadU16LE() { ushort v = (ushort)(_b[Pos] | _b[Pos+1] << 8); Pos += 2; return v; }
    public uint ReadU32LE() { uint v = (uint)(_b[Pos] | _b[Pos+1]<<8 | _b[Pos+2]<<16 | _b[Pos+3]<<24); Pos += 4; return v; }
    public bool TryReadLeb128(out ulong v) { bool ok = Leb128.TryRead(_b[Pos..], out v, out int n); Pos += n; return ok; }
}
