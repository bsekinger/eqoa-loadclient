using System.Numerics;
namespace EqoaLoadClient.Core.Movement;

/// A bounded region the bot may occupy. P0 = axis box; P1 = navmesh-backed
/// (fed by EQOAEmu.ZoneExtractor / EQOA_NavmeshBuilder from the client .esf collision).
public interface IMovementRegion
{
    Vector3 Spawn { get; }
    bool Contains(Vector3 p);
    /// Advance from `current` by ~stepUnits, staying inside; returns new heading (radians).
    Vector3 NextStep(Vector3 current, float stepUnits, out float heading);
}
