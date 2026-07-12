using EqoaLoadClient.Core.Session;
using Xunit;

public class LoadBotJoinTests
{
    [Fact]
    public void Encodes_fields_little_endian()
    {
        byte[] body = LoadBotJoin.Encode(opcode: 0x00000901, botIndex: 3, zoneId: 1,
            x: 100, y: 5, z: -200, classId: 7, level: 30, cluster: 2);
        // opcode u32 LE, botIndex u32 LE, zone u16 LE, x/y/z s32 LE, class u8, level u8, cluster u16 LE
        Assert.Equal(new byte[]{0x01,0x09,0x00,0x00}, body[0..4]);
        Assert.Equal(3, body[4] | body[5]<<8 | body[6]<<16 | body[7]<<24);
        Assert.Equal(1, body[8] | body[9]<<8);
        Assert.Equal(100, BitConverter.ToInt32(body, 10));
        Assert.Equal(-200, BitConverter.ToInt32(body, 18));
        Assert.Equal(7, body[22]);
        Assert.Equal(30, body[23]);
        Assert.Equal(2, body[24] | body[25]<<8);
        Assert.Equal(26, body.Length);
    }
}
