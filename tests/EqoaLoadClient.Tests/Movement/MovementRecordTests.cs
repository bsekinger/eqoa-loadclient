using EqoaLoadClient.Core.Movement;
using EqoaLoadClient.Core.Primitives;
using Xunit;

public class MovementRecordTests
{
    private static MovementState Spawn() => new()
    {
        World = 7,
        X = 0f, Y = 1000f, Z = -4000f,
        Heading = 0f,
        YDelta = 0f,
        AnimState = 0x12,
        Field36 = 0x34,
        Field31 = 0xAABBCCDD,
        Field37 = 0x11223344,
    };

    [Fact]
    public void Record_is_41_bytes()
    {
        var w = new PacketWriter();
        MovementRecord.Write(w, Spawn());
        Assert.Equal(41, w.Length);
    }

    [Fact]
    public void Byte0_is_world_and_position_follows_big_endian()
    {
        var w = new PacketWriter();
        MovementRecord.Write(w, Spawn());
        var b = w.ToArray();
        Assert.Equal(0x07, b[0]);                       // world/zone id u8 (server reads byte[0] as World)
        Assert.Equal(new byte[]{0x1C,0x71,0xC7}, b[1..4]);   // X=0  -> 1C71C7
        Assert.Equal(new byte[]{0xFF,0xFF,0xFF}, b[4..7]);   // Y=1000 (max)
        Assert.Equal(new byte[]{0x00,0x00,0x00}, b[7..10]);  // Z=-4000 (min)
    }

    [Fact]
    public void Trailing_u32_fields_are_little_endian()
    {
        var w = new PacketWriter();
        MovementRecord.Write(w, Spawn());
        var b = w.ToArray();
        // off 31..34 = Field31 LE, off 35 = anim, off 36 = Field36, off 37..40 = Field37 LE
        Assert.Equal(new byte[]{0xDD,0xCC,0xBB,0xAA}, b[31..35]);
        Assert.Equal(0x12, b[35]);
        Assert.Equal(0x34, b[36]);
        Assert.Equal(new byte[]{0x44,0x33,0x22,0x11}, b[37..41]);
    }
}
