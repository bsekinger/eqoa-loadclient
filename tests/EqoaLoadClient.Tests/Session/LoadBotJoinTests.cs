using EqoaLoadClient.Core.Session;
using Xunit;

public class LoadBotJoinTests
{
    [Fact]
    public void Encodes_fields_little_endian()
    {
        byte[] body = LoadBotJoin.Encode(opcode: 0x0BB0, botIndex: 3, worldId: 1,
            x: 100, y: 5, z: -200, classId: 7, level: 30, cluster: 2);
        // opcode u16 LE, botIndex u32 LE, zone u16 LE, x/y/z s32 LE, class u8, level u8, cluster u16 LE = 24 bytes
        Assert.Equal(new byte[]{0xB0,0x0B}, body[0..2]);   // opcode 0x0BB0 u16 LE
        Assert.Equal(3, BitConverter.ToInt32(body, 2));    // botIndex u32
        Assert.Equal(1, body[6] | body[7]<<8);             // worldId u16
        Assert.Equal(100, BitConverter.ToInt32(body, 8));  // x s32
        Assert.Equal(5, BitConverter.ToInt32(body, 12));   // y s32
        Assert.Equal(-200, BitConverter.ToInt32(body, 16));// z s32 (signedness)
        Assert.Equal(7, body[20]);                         // classId
        Assert.Equal(30, body[21]);                        // level
        Assert.Equal(2, body[22] | body[23]<<8);           // cluster u16
        Assert.Equal(24, body.Length);
    }
}
