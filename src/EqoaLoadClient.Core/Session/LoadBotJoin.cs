using EqoaLoadClient.Core.Primitives;

namespace EqoaLoadClient.Core.Session;

public static class LoadBotJoin
{
    /// Payload per docs/protocol/loadbotjoin.md. Opcode is a u16 GameOpcode
    /// (emu assigned 0x0BB0); the body is 24 bytes, all little-endian.
    public static byte[] Encode(ushort opcode, uint botIndex, ushort worldId,
        int x, int y, int z, byte classId, byte level, ushort cluster)
    {
        var w = new PacketWriter(24);
        w.WriteU16LE(opcode);
        w.WriteU32LE(botIndex);
        w.WriteU16LE(worldId);   // world 0..5; the emu's contract table labels this field "zoneId" (byte-identical u16)
        w.WriteS32LE(x); w.WriteS32LE(y); w.WriteS32LE(z);
        w.WriteByte(classId); w.WriteByte(level);
        w.WriteU16LE(cluster);
        return w.ToArray();
    }
}
