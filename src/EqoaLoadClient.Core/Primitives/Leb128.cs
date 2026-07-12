namespace EqoaLoadClient.Core.Primitives;

public static class Leb128
{
    public static void Write(PacketWriter w, ulong value)
    {
        do
        {
            byte b = (byte)(value & 0x7F);
            value >>= 7;
            if (value != 0) b |= 0x80;
            w.WriteByte(b);
        } while (value != 0);
    }

    public static bool TryRead(ReadOnlySpan<byte> src, out ulong value, out int bytesRead)
    {
        value = 0; bytesRead = 0; int shift = 0;
        while (bytesRead < src.Length && shift < 64)
        {
            byte b = src[bytesRead++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return true;
            shift += 7;
        }
        value = 0; bytesRead = 0; return false;
    }
}
