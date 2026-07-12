# LoadBotJoin — load-test bypass opcode (bot → emu)

The load-test bot skips retail auth/login/char-select. After DRDP establishment,
it sends ONE reliable control message on channel `0xfb` carrying `LoadBotJoin`.
The emu injects a ceremony-free but fully combat/cast-capable entity keyed to the
DRDP session and begins accepting the bot's channel-`0x40` movement.

## Wire payload (little-endian scalars)

| off | size | field    | meaning                                                     |
|-----|------|----------|-------------------------------------------------------------|
| 0   | u32  | opcode   | LoadBotJoin opcode — **assigned by the emu registry** (TBD) |
| 4   | u32  | botIndex | 0..N-1, stable per bot                                       |
| 8   | u16  | zoneId   | world/zone: Tunaria, Rathe, Odus, LavaStorm, Plane of Sky, Secrets |
| 10  | s32  | x        | spawn X (world units)                                       |
| 14  | s32  | y        | spawn Y                                                     |
| 18  | s32  | z        | spawn Z                                                     |
| 22  | u8   | classId  | so the injected entity is combat/cast-capable               |
| 23  | u8   | level    | 1..60                                                       |
| 24  | u16  | cluster  | spawn-cluster: bots sharing a value **concentrate** into one proximity hotspot |

Total payload = 26 bytes. Carried as the body of a reliable control message
(type `0xfb`) over the DRDP transport.

## Outer-transport note

The bot's outbound DRDP datagrams use a **minimal outer header**: `HasInstance`
(`0x2000`, carries the InstanceID) + `NoAddrA` (`0x1000`, so no address varints
ride the wire — the emu keys the peer off the UDP source address) + `NewInstance`
(`0x80000`) during establishment. No `addrB`/`addr64` fields. The emu's existing
outer-frame parser handles this (0x1000 set ⇒ addrA absent, per
`eqoa-bridge/findings/systems/drdp-outer-endpoint-framing-crc`).

## Emu handler must

1. Map (peer UDP addr, InstanceID) → a new lightweight entity in `zoneId` at
   (x,y,z), with `classId`/`level` so later combat/cast behaviors work.
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
