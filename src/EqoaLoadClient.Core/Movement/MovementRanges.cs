namespace EqoaLoadClient.Core.Movement;

/// Per-field (min,max) quantization ranges + byte widths for the 0x40 record.
public static class MovementRanges
{
    // position XYZ, 3 bytes each
    public static readonly (float min, float max)[] Pos =
        { (-4000f, 32000f), (-1000f, 1000f), (-4000f, 32000f) };
    // vector A, 2 bytes each (3rd component present)
    public static readonly (float min, float max)[] VecA =
        { (-15.3f, 15.3f), (-84.5f, 3f), (-15.3f, 15.3f) };
    // vector B, 2 bytes each
    public static readonly (float min, float max)[] VecB =
        { (-62.6f, 62.6f), (-12.51f, 12.51f), (-62.52f, 62.52f) };
    // orientation, 1 byte each; 3rd component zero-range -> omitted
    public static readonly (float min, float max)[] Orient =
        { (-3.14159265f, 3.14159265f), (-1.6f, 1.6f), (0f, 0f) };
    public static readonly (float min, float max)[] AngRateA =
        { (-6.4f, 6.4f), (-0.8f, 0.8f), (0f, 0f) };
    public static readonly (float min, float max)[] AngRateB =
        { (-31.2f, 31.2f), (-3.9f, 3.9f), (0f, 0f) };
}
