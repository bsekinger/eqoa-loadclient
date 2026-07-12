# LoadBotJoin — load-test bypass opcode (bot → emu)

The load-test bot skips retail auth/login/char-select. After DRDP establishment,
it sends ONE reliable control message on channel `0xfb` carrying `LoadBotJoin`.
The emu injects a ceremony-free but fully combat/cast-capable entity keyed to the
DRDP session and begins accepting the bot's channel-`0x40` movement.

## Wire payload (little-endian scalars) — FINALIZED with emu 2026-07-12

| off | size | field    | meaning                                                     |
|-----|------|----------|-------------------------------------------------------------|
| 0   | u16  | opcode   | **`0x0BB0`** (emu-assigned `GameOpcode.LoadBotJoin`)         |
| 2   | u32  | botIndex | 0..N-1, stable per bot                                       |
| 6   | u16  | worldId  | **world** (0=Tunaria 1=Rathe 2=Odus 3=LavaStorm 4=PlaneOfSky 5=Secrets). NB: the emu handler reads this same u16 as `zoneId` — byte-identical, just a clearer name on the bot side (a world contains many grid *zones*). |
| 8   | s32  | x        | spawn X (world units) — **must be > 0** (emu bounds-check)  |
| 12  | s32  | y        | spawn Y                                                     |
| 16  | s32  | z        | spawn Z — **must be > 0** (emu bounds-check)                |
| 20  | u8   | classId  | so the injected entity is combat/cast-capable               |
| 21  | u8   | level    | 1..60                                                       |
| 22  | u16  | cluster  | spawn-cluster: bots sharing a value **concentrate** into one proximity hotspot |

Total payload = **24 bytes**. The opcode is a **u16** (`GameOpcode` is a `ushort`;
the emu reads `Read<ushort>()` then dispatches, reading the 22-byte payload from
`botIndex`). Carried as the body of a reliable control message (type `0xfb`) over
the DRDP transport. The emu handler is on branch `loadbotjoin`, gated behind
`EnableLoadBotJoin` (default OFF in production — the bypass spawns an
unauthenticated entity).

## Outer-transport note

The bot's outbound DRDP datagrams use a **minimal outer header**: `HasInstance`
(`0x2000`, carries the InstanceID) + `NoAddrA` (`0x1000`, so no address varints
ride the wire — the emu keys the peer off the UDP source address) + `NewInstance`
(`0x80000`) during establishment. No `addrB`/`addr64` fields. The emu's existing
outer-frame parser handles this (0x1000 set ⇒ addrA absent, per
`eqoa-bridge/findings/systems/drdp-outer-endpoint-framing-crc`).

## Emu handler must

1. Map (peer UDP addr, InstanceID) → a new lightweight entity in world `worldId`
   at (x,y,z), with `classId`/`level` so later combat/cast behaviors work.
2. Start accepting channel-`0x40` movement for that session (see
   `eqoa-bridge/findings/systems/drdp-channel-0x40-movement-payload`).
3. Optionally reply with the assigned entity id (u32) as a reliable control
   message; the bot does not require it for movement.
4. NOT require any account, inventory, char-select, or `0x000D`/`0x0014`
   world-entry handshake.

## Open (finalize before P0 functional run)

- **opcode number** — the emu owns the registry; assign one and report it back.
- whether `cluster` / `level` / `classId` widths suffice for the emu's entity
  template (widen here if needed).
