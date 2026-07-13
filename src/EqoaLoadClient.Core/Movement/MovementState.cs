namespace EqoaLoadClient.Core.Movement;

/// Everything the 41-byte record needs. Vector A/B are prediction data; a wander
/// bot may leave them zero. Field31/36/37 are opaque passthroughs (u32/u8/u32).
public struct MovementState
{
    public byte World;      // [0] u8 = world/zone id; the server reads byte[0] as World. NOT a counter.
    public float X, Y, Z;
    public float Heading;   // [-pi, pi]
    public float YDelta;    // curY - prevY, clamped to [-2000, 2000]
    public byte AnimState;  // 0..0xff
    public byte Field36;
    public uint Field31;
    public uint Field37;

    public System.Numerics.Vector3 VecA;   // default zero
    public System.Numerics.Vector3 VecB;   // default zero
    // Orientation pitch (component 2 of the orient field); yaw uses Heading.
    public float Pitch;
    public float AngRateA1, AngRateA2, AngRateB1, AngRateB2;
}
