# eqoa-loadclient Movement Bot — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A single C# `BotClient` that establishes a DRDP session with EQOAEmu, joins the world via the `LoadBotJoin` bypass, wanders inside a bounded region, streams the 41-byte channel-0x40 movement record with correct acks, and logs out cleanly — validated by unit byte-vectors and a functional run against the emu.

**Architecture:** A `EqoaLoadClient.Core` class library layered exactly like the DRDP findings (Primitives → Transport → Session → Movement → Bot), with an externally-tickable `BotClient.Tick(nowMs)` primitive and an `IBotBehavior` seam whose only P0 implementation is `MovementBehavior`. All wire bytes are built from the RE findings; **quantized movement fields are big-endian, all other scalars little-endian**. A console `Harness` runs one bot and (later) diffs against a PCSX2 capture.

**Tech Stack:** .NET 9, C#, xUnit. No external packages for the core codec. No reference to any EQOAEmu code.

**Source of truth (wire):** `eqoa-bridge/findings/systems/`: `drdp-outer-endpoint-framing-crc`, `drdp-client-emit-contract`, `drdp-sequence-space-seeds`, `drdp-retransmit-cadence`, `drdp-close-fin-wire-shape`, `drdp-channel-0x40-movement-payload`. **Design spec:** `docs/superpowers/specs/2026-07-12-eqoa-loadclient-movement-bot-design.md`.

**Conventions used throughout:** `PacketWriter` is a growable little-endian-by-default byte buffer; big-endian is explicit via `WriteQuantized`. All public codec types live in `EqoaLoadClient.Core`. Tests live in `EqoaLoadClient.Tests` mirroring the source folder.

---

## File Structure

```
eqoa-loadclient/
├── EqoaLoadClient.sln
├── src/EqoaLoadClient.Core/
│   ├── EqoaLoadClient.Core.csproj
│   ├── Primitives/PacketWriter.cs      # growable buffer: byte, LE u16/u32, BE bytes, raw
│   ├── Primitives/PacketReader.cs      # cursor reader: byte, LE u16/u32, LEB128
│   ├── Primitives/Crc32.cs             # reflected CRC-32 + drdp trailer (^0xEE0E612C)
│   ├── Primitives/Leb128.cs            # unsigned LEB128 write / tryRead
│   ├── Movement/Quantizer.cs           # round((v-min)/(max-min)*2^8n), BE, zero-range omit
│   ├── Movement/MovementRanges.cs      # the 0x013e7f10 range table constants
│   ├── Movement/MovementState.cs       # pos/heading/anim struct fed to the record builder
│   ├── Movement/MovementRecord.cs      # the 41-byte channel-0x40 record builder
│   ├── Movement/IMovementRegion.cs     # region abstraction
│   ├── Movement/BoundingBoxRegion.cs   # P0 region + wander step
│   ├── Movement/MovementBehavior.cs    # IBotBehavior #1
│   ├── Transport/OuterFrame.cs         # [src_ep][dst_ep] + per-msg flag header + CRC trailer
│   ├── Transport/Segment.cs            # flags byte + ack fields + segment seq
│   ├── Transport/GameMessage.cs        # type/size/seq/refnum (+XOR-delta) message encode
│   ├── Transport/ChannelState.cs       # per-channel seq + send history (XOR base)
│   ├── Transport/AckState.cs           # inbound cumulative + per-channel ack accounting
│   ├── Transport/DrdpConnection.cs     # seq spaces, ack/flush, retransmit, close/FIN
│   ├── Transport/IUdpChannel.cs        # socket abstraction (injectable for tests)
│   ├── Transport/UdpChannel.cs         # real UdpClient-backed impl
│   ├── Session/Establishment.cs        # NewInstance/InstanceID outer handshake
│   ├── Session/LoadBotJoin.cs          # the bypass opcode encoder
│   ├── Bot/IBotBehavior.cs             # per-tick behavior seam
│   ├── Bot/BotContext.cs               # what a behavior can touch (conn, movement, clock)
│   ├── Bot/BotConfig.cs               # server endpoint, bot index, class/level, region
│   ├── Bot/BotState.cs                 # lifecycle enum
│   └── Bot/BotClient.cs                # Tick(now) primitive + RunAsync wrapper
├── src/EqoaLoadClient.Harness/
│   ├── EqoaLoadClient.Harness.csproj
│   ├── Program.cs                      # single-bot runner CLI
│   └── CaptureDiff.cs                  # PCSX2 capture-diff (built P0, run later)
├── tests/EqoaLoadClient.Tests/
│   ├── EqoaLoadClient.Tests.csproj
│   └── ... (mirrors src, one file per source unit)
└── docs/protocol/loadbotjoin.md        # the bot↔emu contract (Task 0)
```

---

## Task 0: Publish the LoadBotJoin wire contract (unblocks the emu session)

**Files:**
- Create: `docs/protocol/loadbotjoin.md`

- [ ] **Step 1: Write the contract doc**

```markdown
# LoadBotJoin — load-test bypass opcode (bot → emu)

The load-test bot skips retail auth/login/char-select. After DRDP establishment,
it sends ONE reliable control message on channel 0xfb carrying LoadBotJoin. The
emu injects a ceremony-free but fully combat/cast-capable entity keyed to the
DRDP session and begins accepting the bot's channel-0x40 movement.

## Wire payload (little-endian scalars)
| off | size | field    | meaning                                           |
|-----|------|----------|---------------------------------------------------|
| 0   | u32  | opcode   | LoadBotJoin opcode — **assigned by the emu registry** (TBD) |
| 4   | u32  | botIndex | 0..N-1, stable per bot                             |
| 8   | u16  | zoneId   | world/zone: Tunaria/Rathe/Odus/LavaStorm/Sky/Secrets |
| 10  | s32  | x        | spawn X (world units)                             |
| 14  | s32  | y        | spawn Y                                            |
| 18  | s32  | z        | spawn Z                                            |
| 22  | u8   | classId  | so the entity is combat/cast-capable              |
| 23  | u8   | level    | 1..60                                             |
| 24  | u16  | cluster  | spawn-cluster: bots sharing a value concentrate    |

## Emu handler must
1. Map (peer addr, InstanceID) → a new lightweight entity in `zoneId` at (x,y,z),
   with `classId`/`level` so later combat/cast behaviors work.
2. Start accepting channel-0x40 movement for that session (see
   `eqoa-bridge/.../drdp-channel-0x40-movement-payload`).
3. Optionally reply with the assigned entity id (u32) as a reliable control
   message; the bot does not require it for movement.
4. NOT require any account, inventory, char-select, or 0x000D/0x0014 handshake.

## Open (finalize before P0)
- opcode number (emu owns the registry)
- whether `cluster`/`level`/`classId` widths suffice for the emu's entity template
```

- [ ] **Step 2: Commit and push (loadclient), then drop a bridge pointer**

```bash
git add docs/protocol/loadbotjoin.md
git commit -m "docs: LoadBotJoin bot<->emu wire contract"
git push origin main
```

Then in the `eqoa-bridge` repo, add `requests/open/2026-07-12-loadbotjoin-emu-handler.md` with `status: open`, a one-paragraph ask ("emu: implement the LoadBotJoin handler per eqoa-loadclient/docs/protocol/loadbotjoin.md; assign the opcode number and confirm field widths"), commit `re: request emu LoadBotJoin handler`, and push. This is the coordination hand-off; it does not block Tasks 1+.

---

## Task 1: Solution scaffold

**Files:**
- Create: `EqoaLoadClient.sln`, `src/EqoaLoadClient.Core/EqoaLoadClient.Core.csproj`, `src/EqoaLoadClient.Harness/EqoaLoadClient.Harness.csproj`, `tests/EqoaLoadClient.Tests/EqoaLoadClient.Tests.csproj`

- [ ] **Step 1: Create the solution and projects**

```bash
cd C:/Users/bseki/source/repos/eqoa-loadclient
dotnet new sln -n EqoaLoadClient
dotnet new classlib -o src/EqoaLoadClient.Core -f net9.0
dotnet new console  -o src/EqoaLoadClient.Harness -f net9.0
dotnet new xunit    -o tests/EqoaLoadClient.Tests -f net9.0
rm src/EqoaLoadClient.Core/Class1.cs
dotnet sln add src/EqoaLoadClient.Core src/EqoaLoadClient.Harness tests/EqoaLoadClient.Tests
dotnet add src/EqoaLoadClient.Harness reference src/EqoaLoadClient.Core
dotnet add tests/EqoaLoadClient.Tests reference src/EqoaLoadClient.Core
```

- [ ] **Step 2: Enable nullable + implicit usings + LangVersion in Core**

Edit `src/EqoaLoadClient.Core/EqoaLoadClient.Core.csproj` so the `<PropertyGroup>` contains:

```xml
<TargetFramework>net9.0</TargetFramework>
<Nullable>enable</Nullable>
<ImplicitUsings>enable</ImplicitUsings>
<AllowUnsafeBlocks>true</AllowUnsafeBlocks>
```

- [ ] **Step 3: Verify the solution builds**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "chore: scaffold .NET 9 solution (Core/Harness/Tests)"
```

---

## Task 2: `PacketWriter` (the wire buffer)

**Files:**
- Create: `src/EqoaLoadClient.Core/Primitives/PacketWriter.cs`
- Test: `tests/EqoaLoadClient.Tests/Primitives/PacketWriterTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using EqoaLoadClient.Core.Primitives;
using Xunit;

public class PacketWriterTests
{
    [Fact]
    public void Writes_scalars_little_endian_and_raw_big_endian()
    {
        var w = new PacketWriter(16);
        w.WriteByte(0x12);
        w.WriteU16LE(0x3456);          // -> 56 34
        w.WriteU32LE(0x89ABCDEF);      // -> EF CD AB 89
        w.WriteBytesBE(new byte[] { 0x01, 0x02, 0x03 }); // raw, order preserved
        Assert.Equal(
            new byte[] { 0x12, 0x56, 0x34, 0xEF, 0xCD, 0xAB, 0x89, 0x01, 0x02, 0x03 },
            w.ToArray());
    }

    [Fact]
    public void Grows_past_initial_capacity()
    {
        var w = new PacketWriter(2);
        for (int i = 0; i < 100; i++) w.WriteByte((byte)i);
        Assert.Equal(100, w.Length);
        Assert.Equal(99, w.ToArray()[99]);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter PacketWriterTests`
Expected: FAIL (PacketWriter does not exist).

- [ ] **Step 3: Implement `PacketWriter`**

```csharp
namespace EqoaLoadClient.Core.Primitives;

/// Growable byte buffer. Scalars are little-endian (drdp wire default);
/// quantized movement fields are written big-endian via WriteBytesBE by the caller.
public sealed class PacketWriter
{
    private byte[] _buf;
    public int Length { get; private set; }

    public PacketWriter(int capacity = 64) => _buf = new byte[Math.Max(1, capacity)];

    private void Ensure(int extra)
    {
        if (Length + extra <= _buf.Length) return;
        int n = _buf.Length * 2;
        while (n < Length + extra) n *= 2;
        Array.Resize(ref _buf, n);
    }

    public void WriteByte(byte b) { Ensure(1); _buf[Length++] = b; }

    public void WriteU16LE(ushort v) { Ensure(2); _buf[Length++] = (byte)v; _buf[Length++] = (byte)(v >> 8); }

    public void WriteU32LE(uint v)
    {
        Ensure(4);
        _buf[Length++] = (byte)v; _buf[Length++] = (byte)(v >> 8);
        _buf[Length++] = (byte)(v >> 16); _buf[Length++] = (byte)(v >> 24);
    }

    public void WriteS32LE(int v) => WriteU32LE(unchecked((uint)v));

    /// Append bytes verbatim (used for already-ordered data: big-endian quantized fields, payloads).
    public void WriteBytesBE(ReadOnlySpan<byte> src) { Ensure(src.Length); src.CopyTo(_buf.AsSpan(Length)); Length += src.Length; }

    public ReadOnlySpan<byte> AsSpan() => _buf.AsSpan(0, Length);
    public byte[] ToArray() => _buf.AsSpan(0, Length).ToArray();
    public void Reset() => Length = 0;
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test --filter PacketWriterTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: PacketWriter (LE scalars + raw/BE append)"
```

---

## Task 3: `Crc32` (drdp CRC + trailer)

**Files:**
- Create: `src/EqoaLoadClient.Core/Primitives/Crc32.cs`
- Test: `tests/EqoaLoadClient.Tests/Primitives/Crc32Tests.cs`

Finding: reflected CRC-32, poly `0xEDB88320`, init `0xFFFFFFFF`, final XOR `0xFFFFFFFF`. The drdp datagram trailer is `crc ^ 0xEE0E612C`, written **little-endian**.

- [ ] **Step 1: Write the failing test** (vectors precomputed)

```csharp
using EqoaLoadClient.Core.Primitives;
using Xunit;

public class Crc32Tests
{
    [Fact]
    public void Standard_check_value()
        => Assert.Equal(0xCBF43926u, Crc32.Compute("123456789"u8));

    [Fact]
    public void Drdp_trailer_is_crc_xor_constant()
        => Assert.Equal(0xCBF43926u ^ 0xEE0E612Cu, Crc32.Trailer("123456789"u8));
}
```

- [ ] **Step 2: Run to verify it fails** — `dotnet test --filter Crc32Tests` → FAIL.

- [ ] **Step 3: Implement `Crc32`**

```csharp
namespace EqoaLoadClient.Core.Primitives;

public static class Crc32
{
    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var t = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[i] = c;
        }
        return t;
    }

    public static uint Compute(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (byte b in data) crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }

    /// The 4-byte datagram trailer value (written little-endian by OuterFrame).
    public const uint DrdpXor = 0xEE0E612Cu;
    public static uint Trailer(ReadOnlySpan<byte> datagramWithoutTrailer)
        => Compute(datagramWithoutTrailer) ^ DrdpXor;
}
```

- [ ] **Step 4: Run to verify it passes** — `dotnet test --filter Crc32Tests` → PASS.

- [ ] **Step 5: Commit** — `git add -A && git commit -m "feat: drdp CRC-32 + trailer (crc ^ 0xEE0E612C)"`

---

## Task 4: `Leb128`

**Files:**
- Create: `src/EqoaLoadClient.Core/Primitives/Leb128.cs`
- Test: `tests/EqoaLoadClient.Tests/Primitives/Leb128Tests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using EqoaLoadClient.Core.Primitives;
using Xunit;

public class Leb128Tests
{
    [Theory]
    [InlineData(0UL, new byte[] { 0x00 })]
    [InlineData(127UL, new byte[] { 0x7F })]
    [InlineData(128UL, new byte[] { 0x80, 0x01 })]
    [InlineData(300UL, new byte[] { 0xAC, 0x02 })]
    public void Write_matches_expected(ulong value, byte[] expected)
    {
        var w = new PacketWriter();
        Leb128.Write(w, value);
        Assert.Equal(expected, w.ToArray());
    }

    [Fact]
    public void Roundtrip()
    {
        var w = new PacketWriter();
        Leb128.Write(w, 300UL);
        bool ok = Leb128.TryRead(w.AsSpan(), out ulong v, out int n);
        Assert.True(ok); Assert.Equal(300UL, v); Assert.Equal(2, n);
    }
}
```

- [ ] **Step 2: Run to verify it fails** — FAIL.

- [ ] **Step 3: Implement `Leb128`**

```csharp
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
```

- [ ] **Step 4: Run to verify it passes** — PASS.

- [ ] **Step 5: Commit** — `git commit -am "feat: unsigned LEB128 write/tryRead"`

---

## Task 5: `Quantizer`

**Files:**
- Create: `src/EqoaLoadClient.Core/Movement/Quantizer.cs`
- Test: `tests/EqoaLoadClient.Tests/Movement/QuantizerTests.cs`

Finding: `q = clamp(round((v-min)/(max-min) * 2^(8n)), 0, 2^(8n)-1)`, emitted **big-endian** (MSB first). **If `max == min`, emit nothing** (zero-range component omitted — this is why the record is 41 bytes not 44).

- [ ] **Step 1: Write the failing test** (vectors precomputed)

```csharp
using EqoaLoadClient.Core.Movement;
using EqoaLoadClient.Core.Primitives;
using Xunit;

public class QuantizerTests
{
    [Fact]
    public void PosX_zero_in_range()
    {
        var w = new PacketWriter();
        Quantizer.Write(w, 0f, -4000f, 32000f, 3);
        Assert.Equal(new byte[] { 0x1C, 0x71, 0xC7 }, w.ToArray()); // big-endian
    }

    [Fact]
    public void Max_clamps_to_all_ones()
    {
        var w = new PacketWriter();
        Quantizer.Write(w, 1000f, -1000f, 1000f, 3);
        Assert.Equal(new byte[] { 0xFF, 0xFF, 0xFF }, w.ToArray());
    }

    [Fact]
    public void Min_is_zero()
    {
        var w = new PacketWriter();
        Quantizer.Write(w, -4000f, -4000f, 32000f, 3);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x00 }, w.ToArray());
    }

    [Fact]
    public void Heading_zero_one_byte()
    {
        var w = new PacketWriter();
        Quantizer.Write(w, 0f, -MathF.PI, MathF.PI, 1);
        Assert.Equal(new byte[] { 0x80 }, w.ToArray());
    }

    [Fact]
    public void Zero_range_emits_nothing()
    {
        var w = new PacketWriter();
        Quantizer.Write(w, 5f, 0f, 0f, 1);
        Assert.Equal(0, w.Length);
    }
}
```

- [ ] **Step 2: Run to verify it fails** — FAIL.

- [ ] **Step 3: Implement `Quantizer`**

```csharp
using EqoaLoadClient.Core.Primitives;

namespace EqoaLoadClient.Core.Movement;

public static class Quantizer
{
    /// Writes `nbytes` big-endian, or nothing when max==min (zero-range component).
    public static void Write(PacketWriter w, float value, float min, float max, int nbytes)
    {
        float span = max - min;
        if (span == 0f) return; // omitted, matches FUN_012bb048 guard

        long levels = 1L << (8 * nbytes);
        float frac = (value - min) / span;
        if (frac < 0f) frac = 0f; else if (frac > 1f) frac = 1f;
        long q = (long)MathF.Round(frac * levels, MidpointRounding.AwayFromZero);
        if (q > levels - 1) q = levels - 1;

        for (int i = nbytes - 1; i >= 0; i--)      // MSB first
            w.WriteByte((byte)((q >> (i * 8)) & 0xFF));
    }
}
```

- [ ] **Step 4: Run to verify it passes** — PASS.

- [ ] **Step 5: Commit** — `git commit -am "feat: BE quantizer with zero-range omission"`

---

## Task 6: Range table + `MovementState` + the 41-byte record

**Files:**
- Create: `src/EqoaLoadClient.Core/Movement/MovementRanges.cs`, `MovementState.cs`, `MovementRecord.cs`
- Test: `tests/EqoaLoadClient.Tests/Movement/MovementRecordTests.cs`

Finding `drdp-channel-0x40-movement-payload` — exact emit order and widths. Raw u32/u8 fields are **little-endian**; quantized fields big-endian. Total = **41 bytes**.

- [ ] **Step 1: Write the failing test (structure + length + field spot-checks)**

```csharp
using EqoaLoadClient.Core.Movement;
using EqoaLoadClient.Core.Primitives;
using Xunit;

public class MovementRecordTests
{
    private static MovementState Spawn() => new()
    {
        Counter = 7,
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
    public void Byte0_is_counter_and_position_follows_big_endian()
    {
        var w = new PacketWriter();
        MovementRecord.Write(w, Spawn());
        var b = w.ToArray();
        Assert.Equal(0x07, b[0]);                       // counter u8
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
```

- [ ] **Step 2: Run to verify it fails** — FAIL.

- [ ] **Step 3: Implement the three files**

`MovementRanges.cs` (values read from RAM dump `0x013e7f10`):

```csharp
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
```

`MovementState.cs`:

```csharp
namespace EqoaLoadClient.Core.Movement;

/// Everything the 41-byte record needs. Vector A/B are prediction data; a wander
/// bot may leave them zero. Field31/36/37 are opaque passthroughs (u32/u8/u32).
public struct MovementState
{
    public byte Counter;
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
```

`MovementRecord.cs` (emit order from the finding):

```csharp
using EqoaLoadClient.Core.Primitives;

namespace EqoaLoadClient.Core.Movement;

public static class MovementRecord
{
    public static void Write(PacketWriter w, in MovementState s)
    {
        w.WriteByte(s.Counter);                                  // [0] u8

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
```

- [ ] **Step 4: Run to verify it passes** — `dotnet test --filter MovementRecordTests` → PASS (41 bytes; field spot-checks match).

- [ ] **Step 5: Commit** — `git commit -am "feat: 41-byte channel-0x40 movement record + ranges"`

> Note: the byte-exact *full-record* golden vector is deferred to the capture-diff harness (Task 16), which compares against a real PCSX2 capture. Field-level correctness is locked by the quantizer + spot-check tests here.

---

## Task 7: `IMovementRegion` + `BoundingBoxRegion` + wander

**Files:**
- Create: `src/EqoaLoadClient.Core/Movement/IMovementRegion.cs`, `BoundingBoxRegion.cs`
- Test: `tests/EqoaLoadClient.Tests/Movement/BoundingBoxRegionTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System.Numerics;
using EqoaLoadClient.Core.Movement;
using Xunit;

public class BoundingBoxRegionTests
{
    [Fact]
    public void Spawn_is_inside_and_steps_stay_inside()
    {
        var region = new BoundingBoxRegion(
            min: new Vector3(0, 0, 0), max: new Vector3(100, 10, 100),
            spawn: new Vector3(50, 5, 50), seed: 1234);
        Assert.True(region.Contains(region.Spawn));
        var p = region.Spawn;
        for (int i = 0; i < 1000; i++)
        {
            p = region.NextStep(p, stepUnits: 20f, out _);
            Assert.True(region.Contains(p), $"left region at step {i}: {p}");
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails** — FAIL.

- [ ] **Step 3: Implement region + box**

`IMovementRegion.cs`:

```csharp
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
```

`BoundingBoxRegion.cs`:

```csharp
using System.Numerics;
namespace EqoaLoadClient.Core.Movement;

public sealed class BoundingBoxRegion : IMovementRegion
{
    private readonly Vector3 _min, _max;
    private readonly Random _rng;
    private float _heading;

    public Vector3 Spawn { get; }

    public BoundingBoxRegion(Vector3 min, Vector3 max, Vector3 spawn, int seed)
    {
        _min = Vector3.Min(min, max); _max = Vector3.Max(min, max);
        Spawn = Vector3.Clamp(spawn, _min, _max);
        _rng = new Random(seed);
        _heading = (float)(_rng.NextDouble() * 2 * Math.PI - Math.PI);
    }

    public bool Contains(Vector3 p) =>
        p.X >= _min.X && p.X <= _max.X && p.Y >= _min.Y && p.Y <= _max.Y &&
        p.Z >= _min.Z && p.Z <= _max.Z;

    public Vector3 NextStep(Vector3 current, float stepUnits, out float heading)
    {
        // Occasional heading jitter -> plausible wander; reflect off the walls.
        _heading += (float)((_rng.NextDouble() - 0.5) * 0.6);
        var dir = new Vector3(MathF.Cos(_heading), 0, MathF.Sin(_heading));
        var next = current + dir * stepUnits;
        if (next.X < _min.X || next.X > _max.X) { _heading = MathF.PI - _heading; }
        if (next.Z < _min.Z || next.Z > _max.Z) { _heading = -_heading; }
        dir = new Vector3(MathF.Cos(_heading), 0, MathF.Sin(_heading));
        next = Vector3.Clamp(current + dir * stepUnits, _min, _max);
        heading = _heading;
        return next;
    }
}
```

- [ ] **Step 4: Run to verify it passes** — PASS (1000 steps stay inside).

- [ ] **Step 5: Commit** — `git commit -am "feat: IMovementRegion + BoundingBoxRegion wander (P0)"`

---

## Task 8: `OuterFrame` (endpoint header + per-message flag header + CRC)

**Files:**
- Create: `src/EqoaLoadClient.Core/Transport/OuterFrame.cs`
- Test: `tests/EqoaLoadClient.Tests/Transport/OuterFrameTests.cs`

Finding `drdp-outer-endpoint-framing-crc`: datagram = `[src_ep u16 LE][dst_ep u16 LE]`, then per-message headers, then a 4-byte CRC trailer (`crc(datagram-without-trailer) ^ 0xEE0E612C`, LE). Per-message header = `flags_len` LEB128 (`len = value & 0x7FF`, `flags = value & 0xFFFFF800`), optional `InstanceID u32 LE` when `flags & 0x2000`, then body[len]. For the bot's game traffic we emit exactly one message per datagram with `HasInstance (0x2000)` set (+ `NewInstance 0x80000` during establishment).

- [ ] **Step 1: Write the failing test**

```csharp
using EqoaLoadClient.Core.Primitives;
using EqoaLoadClient.Core.Transport;
using Xunit;

public class OuterFrameTests
{
    [Fact]
    public void Wraps_body_with_endpoints_instance_and_crc()
    {
        byte[] body = { 0xDE, 0xAD };
        var dg = OuterFrame.Build(srcEp: 0x0102, dstEp: 0xFFFE,
            flags: 0x2000, instanceId: 0x11223344, body: body);

        // [0..1] src LE, [2..3] dst LE
        Assert.Equal(new byte[]{0x02,0x01,0xFE,0xFF}, dg[0..4]);
        // flags_len = (HasInstance 0x2000 | NoAddrA 0x1000) | len(2) = 0x3002 -> LEB128 82 60
        Assert.Equal(new byte[]{0x82,0x60}, dg[4..6]);
        // InstanceID LE
        Assert.Equal(new byte[]{0x44,0x33,0x22,0x11}, dg[6..10]);
        // body
        Assert.Equal(body, dg[10..12]);
        // trailing 4-byte CRC LE over dg[0..^4]
        uint expect = Crc32.Trailer(dg.AsSpan(0, dg.Length - 4));
        uint got = (uint)(dg[^4] | dg[^3] << 8 | dg[^2] << 16 | dg[^1] << 24);
        Assert.Equal(expect, got);
        Assert.Equal(16, dg.Length); // 4 + 2 + 4 + 2 + 4
    }
}
```

- [ ] **Step 2: Run to verify it fails** — FAIL.

- [ ] **Step 3: Implement `OuterFrame`**

```csharp
using EqoaLoadClient.Core.Primitives;

namespace EqoaLoadClient.Core.Transport;

public static class OuterFrame
{
    public const uint FlagHasInstance = 0x2000;
    public const uint FlagNewInstance = 0x80000;
    public const uint FlagResetConnection = 0x10000;
    public const uint FlagNoAddrA = 0x1000;   // SET => addrA varint absent (receiver uses UDP source addr)

    /// One message per datagram (the bot's pattern). Minimal header: HasInstance
    /// (+ NewInstance during establishment) and FlagNoAddrA so no address fields
    /// ride the wire — the emu keys the peer off the UDP source. Setting 0x1000 is
    /// REQUIRED to omit addrA (the finding's gate is inverted); without it a
    /// conformant parser would read an addrA varint that isn't there. `instanceId`
    /// is emitted when HasInstance is set.
    public static byte[] Build(ushort srcEp, ushort dstEp, uint flags, uint instanceId, ReadOnlySpan<byte> body)
    {
        if (body.Length > 0x7FF) throw new ArgumentException("body too long for one message");
        uint hdrFlags = flags | FlagNoAddrA;                 // suppress addrA; no addrB/addr64
        var w = new PacketWriter(body.Length + 16);
        w.WriteU16LE(srcEp);
        w.WriteU16LE(dstEp);
        Leb128.Write(w, hdrFlags | (uint)body.Length);       // flags_len
        if ((hdrFlags & FlagHasInstance) != 0) w.WriteU32LE(instanceId);
        w.WriteBytesBE(body);
        uint trailer = Crc32.Trailer(w.AsSpan());
        w.WriteU32LE(trailer);
        return w.ToArray();
    }
}
```

- [ ] **Step 4: Run to verify it passes** — PASS.

- [ ] **Step 5: Commit** — `git commit -am "feat: OuterFrame (endpoints + flag header + CRC trailer)"`

---

## Task 9: `Segment` + `GameMessage` (+ XOR-delta) + `ChannelState`

**Files:**
- Create: `src/EqoaLoadClient.Core/Transport/ChannelState.cs`, `GameMessage.cs`, `Segment.cs`
- Test: `tests/EqoaLoadClient.Tests/Transport/GameMessageTests.cs`, `SegmentTests.cs`

Findings `drdp-client-emit-contract` + `drdp-sequence-space-seeds`. Game message wire = `type u8 (== channel)`, `size` (u8, or `0xFF`+u16 if ≥255), `seq u16 LE`, `refnum u8`, `payload`. First message on a channel: seq 1, refnum 0 (raw). Later: seq increments, and if history head seq == ackBase, `refnum = seq - ackBase` and payload is byte-wise XOR vs the base message's payload (raw tail if longer). Segment (for the bot) = `flags byte`, `segment_seq u16 LE` (init 1), then ack fields for set flags, then messages. For P0 the bot only needs to *emit* acks it owes and its movement message.

- [ ] **Step 1: Write the failing tests**

```csharp
using EqoaLoadClient.Core.Transport;
using Xunit;

public class GameMessageTests
{
    [Fact]
    public void First_message_is_raw_seq1_refnum0()
    {
        var chan = new ChannelState(channelType: 0x40);
        byte[] payload = { 1, 2, 3, 4 };
        byte[] msg = chan.EncodeNext(payload);
        // type 0x40, size 4, seq 0001 LE, refnum 0, payload
        Assert.Equal(new byte[]{0x40, 0x04, 0x01,0x00, 0x00, 1,2,3,4}, msg);
    }

    [Fact]
    public void Second_message_xor_deltas_when_base_acked()
    {
        var chan = new ChannelState(0x40);
        chan.EncodeNext(new byte[]{10,20,30,40});   // seq 1
        chan.OnPeerAckedChannelSeq(1);              // ackBase now 1, history head seq 1
        byte[] msg = chan.EncodeNext(new byte[]{10,25,30,44}); // seq 2
        // refnum = 2 - 1 = 1; payload = new XOR base = 00,0F,00,0C
        Assert.Equal(new byte[]{0x40, 0x04, 0x02,0x00, 0x01, 0x00,0x0F,0x00,0x0C}, msg);
    }

    [Fact]
    public void Size_uses_ff_u16_when_large()
    {
        var chan = new ChannelState(0x40);
        byte[] big = new byte[300];
        byte[] msg = chan.EncodeNext(big);
        Assert.Equal(0x40, msg[0]);
        Assert.Equal(0xFF, msg[1]);
        Assert.Equal(300, msg[2] | msg[3] << 8); // u16 LE
    }
}
```

- [ ] **Step 2: Run to verify it fails** — FAIL.

- [ ] **Step 3: Implement `ChannelState` + `GameMessage`**

`ChannelState.cs`:

```csharp
using EqoaLoadClient.Core.Primitives;

namespace EqoaLoadClient.Core.Transport;

/// Per-channel outbound seq + XOR-delta base (drdp-client-emit-contract §2).
public sealed class ChannelState
{
    public byte ChannelType { get; }
    private ushort _nextSeq = 1;      // seq seed = 1 (drdp-sequence-space-seeds)
    private ushort _ackBase = 0;      // highest seq the peer per-channel-acked
    // send history: seq -> payload (last few; base is the ackBase entry)
    private readonly Dictionary<ushort, byte[]> _history = new();

    public ChannelState(byte channelType) => ChannelType = channelType;

    public void OnPeerAckedChannelSeq(ushort seq)
    {
        if (seq > _ackBase) _ackBase = seq;
        // purge history below (seq - 0x20)
        foreach (var k in _history.Keys.Where(k => (ushort)(seq - k) > 0x20).ToList())
            _history.Remove(k);
    }

    public byte[] EncodeNext(ReadOnlySpan<byte> payload)
    {
        ushort seq = _nextSeq++;
        byte refnum = 0;
        byte[] outPayload;

        if (_history.TryGetValue(_ackBase, out var basePayload) && _ackBase != 0)
        {
            int delta = seq - _ackBase;
            if (delta > 0 && delta <= 0x20)
            {
                refnum = (byte)delta;
                outPayload = new byte[payload.Length];
                int n = Math.Min(payload.Length, basePayload.Length);
                for (int i = 0; i < n; i++) outPayload[i] = (byte)(payload[i] ^ basePayload[i]);
                for (int i = n; i < payload.Length; i++) outPayload[i] = payload[i];
            }
            else outPayload = payload.ToArray();
        }
        else outPayload = payload.ToArray();

        _history[seq] = payload.ToArray();
        return GameMessage.Encode(ChannelType, seq, refnum, outPayload);
    }
}
```

`GameMessage.cs`:

```csharp
using EqoaLoadClient.Core.Primitives;

namespace EqoaLoadClient.Core.Transport;

public static class GameMessage
{
    public static byte[] Encode(byte type, ushort seq, byte refnum, ReadOnlySpan<byte> payload)
    {
        var w = new PacketWriter(payload.Length + 6);
        w.WriteByte(type);
        if (payload.Length < 0xFF) w.WriteByte((byte)payload.Length);
        else { w.WriteByte(0xFF); w.WriteU16LE((ushort)payload.Length); }
        w.WriteU16LE(seq);
        w.WriteByte(refnum);
        w.WriteBytesBE(payload);
        return w.ToArray();
    }
}
```

`Segment.cs` (minimal for the bot's emit: flags + seq + ack fields + one message):

```csharp
using EqoaLoadClient.Core.Primitives;

namespace EqoaLoadClient.Core.Transport;

/// Emits the bot's outbound segment. `AckState` supplies which flags/fields to add.
public static class Segment
{
    public static byte[] Build(byte flags, ushort segmentSeq, ReadOnlySpan<byte> ackFields, ReadOnlySpan<byte> messages)
    {
        var w = new PacketWriter(ackFields.Length + messages.Length + 4);
        w.WriteByte(flags);
        // flag 0x40 (echo server conn-id) is prepended by AckState into ackFields when needed.
        w.WriteU16LE(segmentSeq);
        w.WriteBytesBE(ackFields);   // pre-encoded per-flag fields (see AckState, Task 10)
        w.WriteBytesBE(messages);
        return w.ToArray();
    }
}
```

- [ ] **Step 4: Run to verify it passes** — `dotnet test --filter GameMessageTests` → PASS.

- [ ] **Step 5: Commit** — `git commit -am "feat: ChannelState XOR-delta + GameMessage + Segment"`

---

## Task 10: Inbound parse + `AckState`

**Files:**
- Create: `src/EqoaLoadClient.Core/Transport/AckState.cs`, `src/EqoaLoadClient.Core/Primitives/PacketReader.cs`
- Test: `tests/EqoaLoadClient.Tests/Transport/AckStateTests.cs`

The bot must (a) strip inbound outer frame + CRC, (b) read the segment enough to learn the server's segment seq / control-message seq / per-channel seqs it must ack, and (c) build the ack fields for its next outbound segment (flags 0x01 segment ack `conn+0x1148`, 0x02 control-message ack `conn+0x1162`, 0x10 per-channel list terminated by 0xF8). For P0 the bot tracks: highest inbound segment seq, highest inbound control-message seq, and per-channel highest received seq.

- [ ] **Step 1: Write the failing test**

```csharp
using EqoaLoadClient.Core.Transport;
using Xunit;

public class AckStateTests
{
    [Fact]
    public void Builds_segment_and_control_acks()
    {
        var acks = new AckState();
        acks.OnInboundSegmentSeq(5);
        acks.OnInboundControlSeq(2);
        acks.NoteChannelReceived(0x00, 40);

        byte flags = acks.BuildAckFields(out byte[] fields);
        Assert.True((flags & 0x01) != 0);  // segment ack present
        Assert.True((flags & 0x02) != 0);  // control-message ack present
        Assert.True((flags & 0x10) != 0);  // per-channel list present
        // fields: [seg u16=5][ctrl u16=2][chan 0x00][seq u16=40][0xF8]
        Assert.Equal(new byte[]{0x05,0x00, 0x02,0x00, 0x00, 0x28,0x00, 0xF8}, fields);
    }
}
```

- [ ] **Step 2: Run to verify it fails** — FAIL.

- [ ] **Step 3: Implement `PacketReader` and `AckState`**

`PacketReader.cs`:

```csharp
namespace EqoaLoadClient.Core.Primitives;

public ref struct PacketReader
{
    private readonly ReadOnlySpan<byte> _b;
    public int Pos;
    public PacketReader(ReadOnlySpan<byte> b) { _b = b; Pos = 0; }
    public bool AtEnd => Pos >= _b.Length;
    public byte ReadByte() => _b[Pos++];
    public ushort ReadU16LE() { ushort v = (ushort)(_b[Pos] | _b[Pos+1] << 8); Pos += 2; return v; }
    public uint ReadU32LE() { uint v = (uint)(_b[Pos] | _b[Pos+1]<<8 | _b[Pos+2]<<16 | _b[Pos+3]<<24); Pos += 4; return v; }
    public bool TryReadLeb128(out ulong v) { bool ok = Leb128.TryRead(_b[Pos..], out v, out int n); Pos += n; return ok; }
}
```

`AckState.cs`:

```csharp
using EqoaLoadClient.Core.Primitives;

namespace EqoaLoadClient.Core.Transport;

/// Accumulates what the bot owes the server as acks, and encodes the ack fields.
public sealed class AckState
{
    private ushort _segSeq; private bool _seg;
    private ushort _ctrlSeq; private bool _ctrl;
    private readonly SortedDictionary<byte, ushort> _chan = new();

    public void OnInboundSegmentSeq(ushort seq) { _segSeq = seq; _seg = true; }
    public void OnInboundControlSeq(ushort seq) { _ctrlSeq = seq; _ctrl = true; }
    public void NoteChannelReceived(byte chan, ushort seq)
    {
        if (!_chan.TryGetValue(chan, out var cur) || seq > cur) _chan[chan] = seq;
    }
    public bool HasPending => _seg || _ctrl || _chan.Count > 0;

    /// Returns the flags byte and the pre-encoded ack fields (in header order 0x01,0x02,0x10).
    public byte BuildAckFields(out byte[] fields)
    {
        byte flags = 0;
        var w = new PacketWriter(16);
        if (_seg) { flags |= 0x01; w.WriteU16LE(_segSeq); }
        if (_ctrl) { flags |= 0x02; w.WriteU16LE(_ctrlSeq); }
        if (_chan.Count > 0)
        {
            flags |= 0x10;
            foreach (var (c, s) in _chan) { w.WriteByte(c); w.WriteU16LE(s); }
            w.WriteByte(0xF8);
        }
        fields = w.ToArray();
        return flags;
    }
}
```

- [ ] **Step 4: Run to verify it passes** — PASS.

- [ ] **Step 5: Commit** — `git commit -am "feat: inbound PacketReader + AckState field encoder"`

---

## Task 11: `IUdpChannel` + `DrdpConnection` (glue: establishment seqs, flush, close)

**Files:**
- Create: `src/EqoaLoadClient.Core/Transport/IUdpChannel.cs`, `UdpChannel.cs`, `InboundSegment.cs`, `DrdpConnection.cs`
- Test: `tests/EqoaLoadClient.Tests/Transport/InboundSegmentTests.cs`, `DrdpConnectionTests.cs`

`DrdpConnection` owns: endpoint ids, InstanceID, segment seq (init 1, ++ per emitted datagram), the control `ChannelState` (0xfb) and movement `ChannelState` (0x40), `AckState`, and a retransmit list of unacked reliables (resend deadline = lastSend + interval(1000ms) + 100). It exposes `SendReliable(payload)`, `EncodeMovement(payload)`, `Flush(nowMs, IUdpChannel)`, `OnInbound(datagram)`, `Close(IUdpChannel)`. `InboundSegment.Parse` strips the outer header (per `drdp-outer-endpoint-framing-crc`) and decodes the segment so the bot learns (a) what it must ack back and (b) which of its own messages the server acked.

- [ ] **Step 0a: Write the inbound-parser test** (`InboundSegmentTests.cs`)

```csharp
using EqoaLoadClient.Core.Primitives;
using EqoaLoadClient.Core.Transport;
using Xunit;

public class InboundSegmentTests
{
    // Build a server->bot datagram: endpoints, per-msg header (HasInstance),
    // segment {flags=0x01|0x10, segSeq=9, segAck=3, chan 0x00 seq 0x28 + 0xF8},
    // then one control message type 0xFB seq 4.
    private static byte[] BuildServerDatagram()
    {
        var seg = new PacketWriter();
        seg.WriteByte(0x01 | 0x10);      // flags: seg-ack + per-channel list
        seg.WriteU16LE(9);               // segment seq
        seg.WriteU16LE(3);               // flag 0x01: highest bot segment seq the server acked
        seg.WriteByte(0x00); seg.WriteU16LE(0x28); seg.WriteByte(0xF8); // chan 0x00 recv seq 40
        // one control message: type 0xFB, size 1, seq 4, refnum 0, payload {0xAA}
        seg.WriteByte(0xFB); seg.WriteByte(1); seg.WriteU16LE(4); seg.WriteByte(0); seg.WriteByte(0xAA);
        return OuterFrame.Build(srcEp: 0x2001, dstEp: 0x0102,
            flags: OuterFrame.FlagHasInstance, instanceId: 0x1122, body: seg.AsSpan());
    }

    [Fact]
    public void Parses_server_ack_and_received_messages()
    {
        Assert.True(InboundSegment.TryParse(BuildServerDatagram(), out var p));
        Assert.Equal((ushort)0x2001, p.ServerEndpoint);
        Assert.Equal((ushort)9, p.SegmentSeq);
        Assert.True(p.HasSegmentAck);
        Assert.Equal((ushort)3, p.SegmentAck);                 // server acked bot seg seq 3
        Assert.Contains((byte)0x00, p.ChannelReceived.Keys);   // will be ignored by bot; server's recv of chan 0
        Assert.Single(p.ControlMessagesReceived);
        Assert.Equal((ushort)4, p.ControlMessagesReceived[0]); // bot must ack control seq 4
    }
}
```

- [ ] **Step 0b: Implement `InboundSegment`**

```csharp
using EqoaLoadClient.Core.Primitives;

namespace EqoaLoadClient.Core.Transport;

/// Decoded server->bot datagram. Only the fields the P0 bot acts on.
public struct InboundSegment
{
    public ushort ServerEndpoint;
    public ushort SegmentSeq;
    public bool HasSegmentAck; public ushort SegmentAck;       // server acked this bot segment seq
    public bool HasControlAck; public ushort ControlAckBase;   // server acked bot control-msg seq base
    public Dictionary<byte, ushort> ChannelReceived;           // server's per-channel acks of bot game seqs
    public List<ushort> ControlMessagesReceived;               // control-msg seqs the bot must ack
    public List<(byte chan, ushort seq)> GameMessagesReceived; // game msgs the bot must ack

    public static bool TryParse(ReadOnlySpan<byte> datagram, out InboundSegment p)
    {
        p = new InboundSegment
        {
            ChannelReceived = new(), ControlMessagesReceived = new(), GameMessagesReceived = new()
        };
        if (datagram.Length < 8) return false;
        var r = new PacketReader(datagram[..^4]);   // drop CRC trailer

        p.ServerEndpoint = r.ReadU16LE();
        r.ReadU16LE();                              // dst ep (ours)
        if (!r.TryReadLeb128(out ulong flagsLen)) return false;
        uint flags = (uint)(flagsLen & 0xFFFFF800);
        int bodyLen = (int)(flagsLen & 0x7FF);
        if ((flags & 0x2000) != 0) r.ReadU32LE();   // InstanceID
        if ((flags & 0x40000) != 0) r.ReadU32LE();  // data
        if ((flags & 0x8000) != 0) { r.ReadU32LE(); r.ReadU32LE(); } // addr64
        if ((flags & 0x1000) == 0) r.TryReadLeb128(out _);           // addrA (present when 0x1000 clear)
        if ((flags & 0x800) != 0) r.TryReadLeb128(out _);            // addrB
        int bodyStart = r.Pos;

        // ---- segment ----
        byte sflags = r.ReadByte();
        if ((sflags & 0x40) != 0) r.ReadU32LE();     // echoed conn-id
        p.SegmentSeq = r.ReadU16LE();
        if ((sflags & 0x01) != 0) { p.HasSegmentAck = true; p.SegmentAck = r.ReadU16LE();
                                    if ((sflags & 0x04) != 0) r.TryReadLeb128(out _); }
        if ((sflags & 0x02) != 0) { p.HasControlAck = true; p.ControlAckBase = r.ReadU16LE();
                                    if ((sflags & 0x08) != 0) r.TryReadLeb128(out _); }
        if ((sflags & 0x10) != 0)
        {
            while (true)
            {
                byte c = r.ReadByte();
                if (c == 0xF8) break;
                ushort s = r.ReadU16LE();
                p.ChannelReceived[c] = s;
            }
        }

        // ---- messages until end of body ----
        int bodyEnd = bodyStart + bodyLen;
        while (r.Pos < bodyEnd)
        {
            byte type = r.ReadByte();
            int size = r.ReadByte();
            if (size == 0xFF) size = r.ReadU16LE();
            ushort seq = r.ReadU16LE();
            r.ReadByte();                             // refnum (bot doesn't reconstruct payloads for P0)
            r.Pos += size;                            // skip payload
            if (type >= 0xF8) return false;           // guard
            if (type == 0xFB || type == 0xF9) p.ControlMessagesReceived.Add(seq);
            else p.GameMessagesReceived.Add((type, seq));
        }
        return true;
    }
}
```

- [ ] **Step 0c: Run the inbound test** — `dotnet test --filter InboundSegmentTests` → PASS.

- [ ] **Step 1: Write the failing test (with a fake channel)**

```csharp
using EqoaLoadClient.Core.Transport;
using Xunit;

public class DrdpConnectionTests
{
    private sealed class FakeChannel : IUdpChannel
    {
        public List<byte[]> Sent = new();
        public void Send(ReadOnlySpan<byte> dg) => Sent.Add(dg.ToArray());
        public bool TryReceive(out byte[] dg) { dg = Array.Empty<byte>(); return false; }
    }

    [Fact]
    public void First_datagram_has_new_instance_and_segment_seq_1()
    {
        var conn = new DrdpConnection(srcEp: 0x0102, instanceId: 0xAABBCCDD);
        var ch = new FakeChannel();
        conn.SendReliable(new byte[]{ 0x01 }); // e.g. a LoadBotJoin body
        conn.Flush(nowMs: 0, ch);
        Assert.Single(ch.Sent);
        var dg = ch.Sent[0];
        // src ep LE
        Assert.Equal(new byte[]{0x02,0x01}, dg[0..2]);
        // flags_len contains NewInstance|HasInstance -> body wrapped; InstanceID present
        Assert.Contains<byte>(0xDD, dg); // InstanceID low byte appears
    }

    [Fact]
    public void Unacked_reliable_is_retransmitted_after_interval()
    {
        var conn = new DrdpConnection(0x0102, 0xAABBCCDD);
        var ch = new FakeChannel();
        conn.SendReliable(new byte[]{ 0x01 });
        conn.Flush(0, ch);                 // send #1
        conn.Flush(500, ch);               // too soon, no resend
        conn.Flush(1200, ch);              // >= 0 + 1000 + 100 -> resend
        Assert.Equal(2, ch.Sent.Count);
    }
}
```

- [ ] **Step 2: Run to verify it fails** — FAIL.

- [ ] **Step 3: Implement `IUdpChannel`, `UdpChannel`, `DrdpConnection`**

`IUdpChannel.cs`:

```csharp
namespace EqoaLoadClient.Core.Transport;
public interface IUdpChannel
{
    void Send(ReadOnlySpan<byte> datagram);
    bool TryReceive(out byte[] datagram);
}
```

`UdpChannel.cs`:

```csharp
using System.Net;
using System.Net.Sockets;

namespace EqoaLoadClient.Core.Transport;

public sealed class UdpChannel : IUdpChannel, IDisposable
{
    private readonly Socket _s;
    private readonly EndPoint _server;
    private readonly byte[] _rx = new byte[2048];

    public UdpChannel(IPEndPoint server)
    {
        _server = server;
        _s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { Blocking = false };
        _s.Connect(server);
    }

    public void Send(ReadOnlySpan<byte> dg) => _s.Send(dg);

    public bool TryReceive(out byte[] dg)
    {
        dg = Array.Empty<byte>();
        if (_s.Available <= 0) return false;
        int n = _s.Receive(_rx);
        if (n <= 0) return false;
        dg = _rx.AsSpan(0, n).ToArray();
        return true;
    }

    public void Dispose() => _s.Dispose();
}
```

`DrdpConnection.cs`:

```csharp
using EqoaLoadClient.Core.Primitives;

namespace EqoaLoadClient.Core.Transport;

public sealed class DrdpConnection
{
    private const int ResendIntervalMs = 1000, ResendSlackMs = 100;
    private const ushort WildcardDst = 0xFFFE;

    private readonly ushort _srcEp;
    private readonly uint _instanceId;
    private ushort _dstEp = WildcardDst;
    private ushort _segmentSeq = 1;              // seed 1
    private bool _identityConfirmed;             // sets NewInstance until true

    private readonly ChannelState _control = new(0xFB);
    private readonly ChannelState _movement = new(0x40);
    private readonly AckState _acks = new();

    private sealed class Pending { public byte[] Datagram = default!; public long LastSendMs; public bool Acked; }
    private readonly List<Pending> _retransmit = new();
    private byte[]? _pendingReliableMsg;         // encoded control message awaiting first flush
    private byte[]? _pendingMovementMsg;         // encoded channel-0x40 message awaiting flush (not retransmitted)

    public DrdpConnection(ushort srcEp, uint instanceId) { _srcEp = srcEp; _instanceId = instanceId; }

    public void SendReliable(ReadOnlySpan<byte> payload) => _pendingReliableMsg = _control.EncodeNext(payload);
    /// Queues a channel-0x40 movement message for the next flush and returns it (metrics hook).
    public byte[] SendMovement(ReadOnlySpan<byte> payload)
    { var m = _movement.EncodeNext(payload); _pendingMovementMsg = m; return m; }

    public void OnInbound(ReadOnlySpan<byte> datagram)
    {
        if (!InboundSegment.TryParse(datagram, out var p)) return;

        // Learn the server's endpoint id; identity is confirmed once we hear back.
        _dstEp = p.ServerEndpoint;
        _identityConfirmed = true;

        // (a) Acks the bot OWES the server:
        _acks.OnInboundSegmentSeq(p.SegmentSeq);
        foreach (var seq in p.ControlMessagesReceived) _acks.OnInboundControlSeq(seq);
        foreach (var (chan, seq) in p.GameMessagesReceived) _acks.NoteChannelReceived(chan, seq);

        // (b) The server's acks OF the bot's messages -> clear retransmit + advance XOR base:
        if (p.HasControlAck)
        {
            _control.OnPeerAckedChannelSeq(p.ControlAckBase);
            foreach (var pend in _retransmit) pend.Acked = true;   // control reliables acked up to base
        }
        if (p.ChannelReceived.TryGetValue(0x40, out var movAck)) _movement.OnPeerAckedChannelSeq(movAck);
    }

    public void NoteControlAcked(ushort seq) => _control.OnPeerAckedChannelSeq(seq);
    public void NoteMovementAcked(ushort seq) => _movement.OnPeerAckedChannelSeq(seq);

    public void Flush(long nowMs, IUdpChannel ch)
    {
        // 1) retransmit due unacked reliables
        foreach (var p in _retransmit)
            if (!p.Acked && nowMs - p.LastSendMs >= ResendIntervalMs + ResendSlackMs)
            { ch.Send(p.Datagram); p.LastSendMs = nowMs; }

        // 2) build a fresh segment if we have any message or owe acks
        byte ackFlags = _acks.BuildAckFields(out byte[] ackFields);
        var msgBuf = new PacketWriter(64);
        if (_pendingReliableMsg != null) msgBuf.WriteBytesBE(_pendingReliableMsg);
        if (_pendingMovementMsg != null) msgBuf.WriteBytesBE(_pendingMovementMsg);
        bool hasMsg = msgBuf.Length > 0;
        if (!hasMsg && ackFlags == 0) return;

        byte[] segment = Segment.Build(ackFlags, _segmentSeq, ackFields, msgBuf.AsSpan());
        uint outerFlags = OuterFrame.FlagHasInstance | (_identityConfirmed ? 0 : OuterFrame.FlagNewInstance);
        byte[] dg = OuterFrame.Build(_srcEp, _dstEp, outerFlags, _instanceId, segment);
        ch.Send(dg);
        _segmentSeq++;

        // Only the reliable message is retransmitted; movement is superseded by the next tick.
        // (The establishment join is sent before movement starts, so its retransmit datagram is join-only.)
        if (_pendingReliableMsg != null)
            _retransmit.Add(new Pending { Datagram = dg, LastSendMs = nowMs });
        _pendingReliableMsg = null;
        _pendingMovementMsg = null;
    }

    public void Close(IUdpChannel ch)
    {
        // FIN = outer ResetConnection | HasInstance + InstanceID (best-effort).
        byte[] dg = OuterFrame.Build(_srcEp, _dstEp,
            OuterFrame.FlagResetConnection | OuterFrame.FlagHasInstance, _instanceId, ReadOnlySpan<byte>.Empty);
        ch.Send(dg);
    }
}
```

> Note: `OnInbound` uses the complete `InboundSegment` parser, so the bot both acks what it owes (segment/control/game seqs) and clears its retransmit + advances its XOR-base from the server's acks — this is what keeps the session off the 60 s dead-peer reaper. Payload *reconstruction* of inbound game messages (XOR-delta decode) is intentionally skipped for P0: the bot doesn't consume other entities' positions, only acks them. Add it only if a later behavior needs inbound world state.

- [ ] **Step 4: Run to verify it passes** — `dotnet test --filter DrdpConnectionTests` → PASS.

- [ ] **Step 5: Commit** — `git commit -am "feat: DrdpConnection (establishment, flush, retransmit, close) + UdpChannel"`

---

## Task 12: `LoadBotJoin` + `Establishment` wiring

**Files:**
- Create: `src/EqoaLoadClient.Core/Session/LoadBotJoin.cs`, `Session/Establishment.cs`
- Test: `tests/EqoaLoadClient.Tests/Session/LoadBotJoinTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
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
```

- [ ] **Step 2: Run to verify it fails** — FAIL.

- [ ] **Step 3: Implement**

`LoadBotJoin.cs`:

```csharp
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
```

`Establishment.cs` — thin helper documenting the sequence (the mechanics live in `DrdpConnection`):

```csharp
namespace EqoaLoadClient.Core.Session;

/// The bot's join sequence: DrdpConnection.SendReliable(LoadBotJoin body) on the
/// control channel; the first Flush carries NewInstance|HasInstance + InstanceID,
/// establishing the session keyed by (addr, InstanceID). No login/char-select.
public static class Establishment
{
    public const uint DefaultInstanceSeed = 0x00010000; // any stable per-bot value
}
```

- [ ] **Step 4: Run to verify it passes** — PASS.

- [ ] **Step 5: Commit** — `git commit -am "feat: LoadBotJoin encoder + establishment wiring"`

---

## Task 13: `IBotBehavior` + `MovementBehavior`

**Files:**
- Create: `src/EqoaLoadClient.Core/Bot/IBotBehavior.cs`, `BotContext.cs`, `Movement/MovementBehavior.cs`
- Test: `tests/EqoaLoadClient.Tests/Bot/MovementBehaviorTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System.Numerics;
using EqoaLoadClient.Core.Bot;
using EqoaLoadClient.Core.Movement;
using EqoaLoadClient.Core.Transport;
using Xunit;

public class MovementBehaviorTests
{
    [Fact]
    public void Emits_a_movement_message_each_interval_and_stays_in_region()
    {
        var region = new BoundingBoxRegion(new Vector3(0,0,0), new Vector3(100,10,100), new Vector3(50,5,50), 7);
        var conn = new DrdpConnection(0x0102, 0xAABBCCDD);
        var ctx = new BotContext(conn, region, intervalMs: 100);
        var beh = new MovementBehavior();

        int emitted = 0;
        ctx.OnMovementEncoded = _ => emitted++;
        for (long t = 0; t <= 1000; t += 100) beh.Tick(t, ctx);
        Assert.True(emitted >= 10);            // ~one per 100ms
        Assert.True(region.Contains(ctx.Position));
    }
}
```

- [ ] **Step 2: Run to verify it fails** — FAIL.

- [ ] **Step 3: Implement**

`IBotBehavior.cs`:

```csharp
namespace EqoaLoadClient.Core.Bot;
public interface IBotBehavior { void Tick(long nowMs, BotContext ctx); }
```

`BotContext.cs`:

```csharp
using System.Numerics;
using EqoaLoadClient.Core.Movement;
using EqoaLoadClient.Core.Transport;

namespace EqoaLoadClient.Core.Bot;

/// The surface a behavior may touch: the connection, its region, the clock cadence,
/// and mutable movement state. Keeps behaviors decoupled from transport internals.
public sealed class BotContext
{
    public DrdpConnection Conn { get; }
    public IMovementRegion Region { get; }
    public int IntervalMs { get; }
    public Vector3 Position { get; set; }
    public MovementState State;
    public long LastMovementMs { get; set; } = -100_000; // safe sentinel (avoids overflow on first tick)
    public Action<byte[]>? OnMovementEncoded;   // test/metrics hook

    public BotContext(DrdpConnection conn, IMovementRegion region, int intervalMs)
    { Conn = conn; Region = region; IntervalMs = intervalMs; Position = region.Spawn; State.Counter = 0; }
}
```

`MovementBehavior.cs`:

```csharp
using EqoaLoadClient.Core.Bot;
using EqoaLoadClient.Core.Primitives;

namespace EqoaLoadClient.Core.Movement;

public sealed class MovementBehavior : IBotBehavior
{
    public void Tick(long nowMs, BotContext ctx)
    {
        if (nowMs - ctx.LastMovementMs < ctx.IntervalMs) return;
        ctx.LastMovementMs = nowMs;

        var prev = ctx.Position;
        var next = ctx.Region.NextStep(prev, stepUnits: 30f, out float heading);
        ctx.Position = next;

        ctx.State.Counter++;
        ctx.State.X = next.X; ctx.State.Y = next.Y; ctx.State.Z = next.Z;
        ctx.State.Heading = heading;
        ctx.State.YDelta = Math.Clamp(next.Y - prev.Y, -2000f, 2000f);
        ctx.State.AnimState = 0; // idle/run state; 0 is safe for P0

        var w = new PacketWriter(48);
        MovementRecord.Write(w, ctx.State);
        byte[] msg = ctx.Conn.SendMovement(w.AsSpan());   // queues it for the next Flush
        ctx.OnMovementEncoded?.Invoke(msg);
    }
}
```

- [ ] **Step 4: Run to verify it passes** — PASS.

- [ ] **Step 5: Commit** — `git commit -am "feat: IBotBehavior seam + MovementBehavior"`

---

## Task 14: `BotClient` (Tick primitive + RunAsync + state machine)

**Files:**
- Create: `src/EqoaLoadClient.Core/Bot/BotConfig.cs`, `BotState.cs`, `BotClient.cs`
- Test: `tests/EqoaLoadClient.Tests/Bot/BotClientTests.cs`

- [ ] **Step 1: Write the failing test (drives Tick with a fake channel)**

```csharp
using System.Numerics;
using EqoaLoadClient.Core.Bot;
using EqoaLoadClient.Core.Movement;
using EqoaLoadClient.Core.Transport;
using Xunit;

public class BotClientTests
{
    private sealed class FakeChannel : IUdpChannel
    {
        public List<byte[]> Sent = new();
        public void Send(ReadOnlySpan<byte> dg) => Sent.Add(dg.ToArray());
        public bool TryReceive(out byte[] dg) { dg = Array.Empty<byte>(); return false; }
    }

    [Fact]
    public void Join_then_movement_then_logout()
    {
        var cfg = new BotConfig
        {
            SrcEndpoint = 0x0102, InstanceId = 0x00010000, BotIndex = 1,
            ZoneId = 1, ClassId = 7, Level = 30, Cluster = 0,
            JoinOpcode = 0x00000901, IntervalMs = 100,
            Region = new BoundingBoxRegion(new Vector3(0,0,0), new Vector3(100,10,100), new Vector3(50,5,50), 1),
        };
        var ch = new FakeChannel();
        var bot = new BotClient(cfg, ch);

        bot.Tick(0);          // establishment: sends join datagram
        Assert.NotEmpty(ch.Sent);
        Assert.Equal(BotState.InWorld, bot.State);

        int before = ch.Sent.Count;
        for (long t = 100; t <= 1000; t += 100) bot.Tick(t);
        Assert.True(ch.Sent.Count > before);   // movement datagrams flowed

        bot.Logout();
        Assert.Equal(BotState.Closed, bot.State);
    }
}
```

- [ ] **Step 2: Run to verify it fails** — FAIL.

- [ ] **Step 3: Implement `BotConfig`, `BotState`, `BotClient`**

`BotState.cs`:

```csharp
namespace EqoaLoadClient.Core.Bot;
public enum BotState { New, Establishing, InWorld, Closed }
```

`BotConfig.cs`:

```csharp
using EqoaLoadClient.Core.Movement;

namespace EqoaLoadClient.Core.Bot;

public sealed class BotConfig
{
    public ushort SrcEndpoint { get; init; }
    public uint InstanceId { get; init; }
    public uint BotIndex { get; init; }
    public ushort ZoneId { get; init; }
    public byte ClassId { get; init; }
    public byte Level { get; init; }
    public ushort Cluster { get; init; }
    public uint JoinOpcode { get; init; }          // from the emu registry
    public int IntervalMs { get; init; } = 100;
    public required IMovementRegion Region { get; init; }
}
```

`BotClient.cs`:

```csharp
using EqoaLoadClient.Core.Movement;
using EqoaLoadClient.Core.Session;
using EqoaLoadClient.Core.Transport;

namespace EqoaLoadClient.Core.Bot;

public sealed class BotClient
{
    private readonly BotConfig _cfg;
    private readonly IUdpChannel _ch;
    private readonly DrdpConnection _conn;
    private readonly BotContext _ctx;
    private readonly IBotBehavior _movement = new MovementBehavior();

    public BotState State { get; private set; } = BotState.New;

    public BotClient(BotConfig cfg, IUdpChannel ch)
    {
        _cfg = cfg; _ch = ch;
        _conn = new DrdpConnection(cfg.SrcEndpoint, cfg.InstanceId);
        _ctx = new BotContext(_conn, cfg.Region, cfg.IntervalMs);
    }

    /// One unit of work. The fleet (or RunAsync) calls this on its clock.
    public void Tick(long nowMs)
    {
        // drain inbound
        while (_ch.TryReceive(out var dg)) _conn.OnInbound(dg);

        switch (State)
        {
            case BotState.New:
                var spawn = _cfg.Region.Spawn;
                byte[] join = LoadBotJoin.Encode(_cfg.JoinOpcode, _cfg.BotIndex, _cfg.ZoneId,
                    (int)spawn.X, (int)spawn.Y, (int)spawn.Z, _cfg.ClassId, _cfg.Level, _cfg.Cluster);
                _conn.SendReliable(join);
                _conn.Flush(nowMs, _ch);
                State = BotState.InWorld;   // P0: proceed to movement immediately (emu injects entity)
                break;

            case BotState.InWorld:
                _movement.Tick(nowMs, _ctx);
                _conn.Flush(nowMs, _ch);
                break;
        }
    }

    public void Logout()
    {
        _conn.Close(_ch);
        State = BotState.Closed;
    }

    /// Convenience self-driving loop for standalone/harness use.
    public async Task RunAsync(CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!ct.IsCancellationRequested)
        {
            Tick(sw.ElapsedMilliseconds);
            try { await Task.Delay(Math.Max(5, _cfg.IntervalMs / 2), ct); } catch (OperationCanceledException) { break; }
        }
        Logout();
    }
}
```

- [ ] **Step 4: Run to verify it passes** — `dotnet test --filter BotClientTests` → PASS.

- [ ] **Step 5: Run the full suite** — `dotnet test` → all PASS.

- [ ] **Step 6: Commit** — `git commit -am "feat: BotClient tickable core + RunAsync + lifecycle"`

---

## Task 15: Harness — single-bot functional runner

**Files:**
- Modify: `src/EqoaLoadClient.Harness/Program.cs`

- [ ] **Step 1: Implement the runner**

```csharp
using System.Net;
using System.Numerics;
using EqoaLoadClient.Core.Bot;
using EqoaLoadClient.Core.Movement;
using EqoaLoadClient.Core.Transport;

// usage: Harness <serverIp> <port> <joinOpcodeHex> [zoneId] [x y z]
var ip = IPAddress.Parse(args[0]);
int port = int.Parse(args[1]);
uint opcode = Convert.ToUInt32(args[2], 16);
ushort zone = args.Length > 3 ? ushort.Parse(args[3]) : (ushort)1;
var spawn = args.Length > 6 ? new Vector3(int.Parse(args[4]), int.Parse(args[5]), int.Parse(args[6])) : new Vector3(0, 0, 0);

var region = new BoundingBoxRegion(spawn - new Vector3(500,10,500), spawn + new Vector3(500,10,500), spawn, seed: 1);
using var ch = new UdpChannel(new IPEndPoint(ip, port));
var cfg = new BotConfig
{
    SrcEndpoint = 0x0102, InstanceId = 0x00010000, BotIndex = 1,
    ZoneId = zone, ClassId = 7, Level = 30, Cluster = 0,
    JoinOpcode = opcode, IntervalMs = 100, Region = region,
};
var bot = new BotClient(cfg, ch);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
Console.WriteLine($"bot connecting to {ip}:{port} zone {zone} spawn {spawn}");
var sw = System.Diagnostics.Stopwatch.StartNew();
while (!cts.IsCancellationRequested)
{
    bot.Tick(sw.ElapsedMilliseconds);
    Console.Write($"\rstate={bot.State} t={sw.Elapsed:mm\\:ss}   ");
    await Task.Delay(50, cts.Token).ContinueWith(_ => { });
}
bot.Logout();
Console.WriteLine("\nlogged out");
```

- [ ] **Step 2: Build** — `dotnet build` → succeeds.

- [ ] **Step 3: Functional smoke (requires the emu + a LoadBotJoin handler)**

Run (once the emu handler + opcode exist): `dotnet run --project src/EqoaLoadClient.Harness -- 127.0.0.1 <port> <opcodeHex> 1 0 0 0`
Expected: `state=InWorld`, the process runs > 130 s without the emu dead-peer-reaping it (proves acks keep it alive), and the emu shows the bot entity moving in its zone. Record the result.

- [ ] **Step 4: Commit** — `git commit -am "feat: single-bot functional harness runner"`

---

## Task 16: Capture-diff conformance harness (built now, run later)

**Files:**
- Create: `src/EqoaLoadClient.Harness/CaptureDiff.cs`
- Test: `tests/EqoaLoadClient.Tests/Harness/CaptureDiffTests.cs`

- [ ] **Step 1: Write the failing test (self-diff is identical)**

```csharp
using EqoaLoadClient.Core.Transport;
using EqoaLoadClient.Harness;
using Xunit;

public class CaptureDiffTests
{
    [Fact]
    public void Identical_datagrams_report_no_divergence()
    {
        byte[] dg = OuterFrame.Build(0x0102, 0xFFFE, OuterFrame.FlagHasInstance, 0x11223344, new byte[]{1,2,3});
        var result = CaptureDiff.CompareDatagram(dg, dg);
        Assert.True(result.Match);
        Assert.Null(result.FirstDivergenceOffset);
    }

    [Fact]
    public void Divergence_reports_first_offset()
    {
        byte[] a = OuterFrame.Build(0x0102, 0xFFFE, OuterFrame.FlagHasInstance, 0x11223344, new byte[]{1,2,3});
        byte[] b = (byte[])a.Clone(); b[10] ^= 0xFF;
        var result = CaptureDiff.CompareDatagram(a, b);
        Assert.False(result.Match);
        Assert.Equal(10, result.FirstDivergenceOffset);
    }
}
```

- [ ] **Step 2: Run to verify it fails** — FAIL.

- [ ] **Step 3: Implement `CaptureDiff`** (byte compare now; field-decode + per-session normalization is the run-time enhancement)

```csharp
namespace EqoaLoadClient.Harness;

public sealed record DiffResult(bool Match, int? FirstDivergenceOffset, string? Note);

public static class CaptureDiff
{
    /// P0: exact byte compare (after the caller normalizes per-session fields:
    /// InstanceID, seq counters, timestamps, CRC). Field-level decode is layered
    /// on when the first real PCSX2 capture is available.
    public static DiffResult CompareDatagram(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual)
    {
        int n = Math.Min(expected.Length, actual.Length);
        for (int i = 0; i < n; i++)
            if (expected[i] != actual[i])
                return new DiffResult(false, i, $"expected 0x{expected[i]:X2} got 0x{actual[i]:X2}");
        if (expected.Length != actual.Length)
            return new DiffResult(false, n, $"length {expected.Length} vs {actual.Length}");
        return new DiffResult(true, null, null);
    }
}
```

Add `<ProjectReference>` from Tests to Harness so the test compiles:
```bash
dotnet add tests/EqoaLoadClient.Tests reference src/EqoaLoadClient.Harness
```

- [ ] **Step 4: Run to verify it passes** — PASS.

- [ ] **Step 5: Commit** — `git commit -am "feat: capture-diff harness scaffold (byte compare)"`

---

## Definition of done (P0)

- `dotnet test` green: CRC, LEB128, quantizer, 41-byte record, region wander, outer frame, game message/XOR-delta, ack fields, DrdpConnection, LoadBotJoin, MovementBehavior, BotClient, capture-diff.
- One bot run via the Harness against the emu (with the `LoadBotJoin` handler + assigned opcode) stays `InWorld` past the 60 s dead-peer window and shows movement in-zone.
- `LoadBotJoin` contract published; bridge request filed for the emu handler.

## Fast-follows (post-P0, not blocking)

1. Collision/navmesh-backed `IMovementRegion` from the client `.esf` via `ZoneExtractor`/`NavmeshBuilder`.
2. Harden `DrdpConnection.OnInbound` segment parse against a real emu ack stream if the functional run shows withheld acks.
3. Run the capture-diff against a real PCSX2 capture (produced on request); add field-level decode + per-session normalization.
4. Fleet integration: the emu session's shared `Tick` scheduler + ladder + metrics referencing `EqoaLoadClient.Core`.
