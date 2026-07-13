using EqoaLoadClient.Core.Primitives;

namespace EqoaLoadClient.Core.Movement;

public static class MovementRecord
{
    public static void Write(PacketWriter w, in MovementState s)
    {
        w.WriteByte(s.World);                                    // [0] u8 = world/zone id (server decodes byte[0] as World; a counter here reads as a bogus world -> zone-change fault)

        // [1..10] position XYZ, 3 bytes each BE
        Quantizer.Write(w, s.X, MovementRanges.Pos[0].min, MovementRanges.Pos[0].max, 3);
        Quantizer.Write(w, s.Y, MovementRanges.Pos[1].min, MovementRanges.Pos[1].max, 3);
        Quantizer.Write(w, s.Z, MovementRanges.Pos[2].min, MovementRanges.Pos[2].max, 3);

        // [10..16] vector A, 2 bytes each
        WriteVec(w, s.VecA, MovementRanges.VecA, 2);
        // [16..22] vector B, 2 bytes each
        WriteVec(w, s.VecB, MovementRanges.VecB, 2);

        // [22..24] orientation: yaw + pitch (3rd zero-range omitted), 1 byte each
        Quantizer.Write(w, s.Heading, MovementRanges.Orient[0].min, MovementRanges.Orient[0].max, 1);
        Quantizer.Write(w, s.Pitch,   MovementRanges.Orient[1].min, MovementRanges.Orient[1].max, 1);
        Quantizer.Write(w, 0f,        MovementRanges.Orient[2].min, MovementRanges.Orient[2].max, 1); // omitted

        // [24..26] ang-rate A (2 present), [26..28] ang-rate B (2 present)
        Quantizer.Write(w, s.AngRateA1, MovementRanges.AngRateA[0].min, MovementRanges.AngRateA[0].max, 1);
        Quantizer.Write(w, s.AngRateA2, MovementRanges.AngRateA[1].min, MovementRanges.AngRateA[1].max, 1);
        Quantizer.Write(w, 0f,          MovementRanges.AngRateA[2].min, MovementRanges.AngRateA[2].max, 1);
        Quantizer.Write(w, s.AngRateB1, MovementRanges.AngRateB[0].min, MovementRanges.AngRateB[0].max, 1);
        Quantizer.Write(w, s.AngRateB2, MovementRanges.AngRateB[1].min, MovementRanges.AngRateB[1].max, 1);
        Quantizer.Write(w, 0f,          MovementRanges.AngRateB[2].min, MovementRanges.AngRateB[2].max, 1);

        // [28] heading (standalone), [29..31] Y-delta
        Quantizer.Write(w, s.Heading, -3.14159265f, 3.14159265f, 1);
        Quantizer.Write(w, s.YDelta, -2000f, 2000f, 2);

        w.WriteU32LE(s.Field31);   // [31..35] raw u32 LE
        w.WriteByte(s.AnimState);  // [35] u8
        w.WriteByte(s.Field36);    // [36] u8
        w.WriteU32LE(s.Field37);   // [37..41] raw u32 LE
    }

    private static void WriteVec(PacketWriter w, System.Numerics.Vector3 v,
        (float min, float max)[] r, int nbytes)
    {
        Quantizer.Write(w, v.X, r[0].min, r[0].max, nbytes);
        Quantizer.Write(w, v.Y, r[1].min, r[1].max, nbytes);
        Quantizer.Write(w, v.Z, r[2].min, r[2].max, nbytes);
    }
}
