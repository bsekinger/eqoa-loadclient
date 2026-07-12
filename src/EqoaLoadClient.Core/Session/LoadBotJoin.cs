using EqoaLoadClient.Core.Primitives;

namespace EqoaLoadClient.Core.Session;

public static class LoadBotJoin
{
    /// Payload per docs/protocol/loadbotjoin.md. Opcode assigned by the emu registry.
    public static byte[] Encode(uint opcode, uint botIndex, ushort zoneId,
        int x, int y, int z, byte classId, byte level, ushort cluster)
    {
        var w = new PacketWriter(26);
        w.WriteU32LE(opcode);
        w.WriteU32LE(botIndex);
        w.WriteU16LE(zoneId);
        w.WriteS32LE(x); w.WriteS32LE(y); w.WriteS32LE(z);
        w.WriteByte(classId); w.WriteByte(level);
        w.WriteU16LE(cluster);
        return w.ToArray();
    }
}
