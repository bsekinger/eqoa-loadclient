# eqoa-loadclient — movement-only bot client core (design)

**Date:** 2026-07-12
**Status:** approved (brainstorming) → pending implementation plan
**Repo:** `github.com/bsekinger/eqoa-loadclient`

## Goal

A C# (.NET 9) fleet of lightweight virtual EQOA clients that speak real DRDP to
the EQOAEmu server, to load-test it and baseline capacity before the
transport-decoupling work. This spec covers the **movement-only** milestone: a
single bot that establishes a DRDP session, joins the world via a lightweight
bypass (no auth/char-select), wanders, acks correctly, and logs out — built as a
class-library **core** the emu's .NET fleet layer hosts in-process.

The first milestone bar is **functional**: one bot completes the full lifecycle
against the emu without being dead-peer-reaped. Byte-level conformance against a
real PCSX2 capture is a fast follow (captures produced on request).

## The ground-truth / conformance-oracle principle

The bot is built **from the client** (the RE transport findings in `eqoa-bridge`),
**not** from the emu's transport code. If it borrowed the server's codec,
bot↔server agreement would be circular — a shared wire misunderstanding would
pass on both ends and only fail against a real PS2 client. A client-sourced core
makes the bot a **conformance oracle** as well as a load generator: bot + server
agreeing *and* matching a real PCSX2 capture validates all three.

**Reuse rule:** the core references **no** emu code. The only whitelisted
primitives (CRC `crc32 ^ 0xEE0E612C`, LEB128) are re-derived here from the
findings, not copied. In particular the core does **not** assume the emu's
"RLE state-channel codec" — channel 0x40 is quantized fixed-fields + refnum
XOR-delta, never RLE on send (see finding `drdp-channel-0x40-movement-payload`).

## Source findings (the wire spec)

All in `eqoa-bridge/findings/systems/`:
`drdp-outer-endpoint-framing-crc` (outer frame, CRC, **little-endian** rule),
`drdp-client-emit-contract` (segment flags, refnum XOR-delta, channels, ack
grammar), `drdp-sequence-space-seeds` (all seqs start at 1), `drdp-retransmit-cadence`
(RTO/interval, prompt-ack lever), `drdp-close-fin-wire-shape` (close = outer
ResetConnection|HasInstance, best-effort), `drdp-zonein-movement-gating`
(0x000D→0x0014 world-entry, movement on channel 0x40), and
`drdp-channel-0x40-movement-payload` (the 41-byte record).

## Architecture

### Concurrency: externally-tickable core

The core's primitive is **`BotClient.Tick(long nowMs)`** — one unit of work:
send a movement update if due, drain inbound datagrams, emit acks, service
retransmit/close timers. The bot is a pure state machine driven by an external
clock; it does not own a timer.

- **Standalone / small N:** `RunAsync(CancellationToken)` is a thin convenience
  wrapper that calls `Tick` on a timer.
- **Fleet / large N:** a single tick scheduler (one time source) calls `Tick` on
  all due bots from one or a few threads.

This makes 300 → 600 → 1000+ a *fleet scheduling knob*, not a core change. The
core neither knows nor cares how many peers exist. Each bot owns **one** UDP
socket (300–600 UDP sockets is trivial for .NET async I/O; no shared-socket
demux, which is only justified at thousands).

**Hot-path discipline (matters at scale):** build→XOR-delta→send allocates
nothing per tick — pooled/reused send buffers, `Span`/`stackalloc` for scratch,
no LINQ in the tick path. At 600 bots × ~10 Hz = ~6000 sends/sec, per-tick GC
would perturb the very thing being measured.

### Assembly structure

- **`EqoaLoadClient.Core`** (class library, **no emu reference**):
  - `Primitives/` — `Crc32` (`crc32 ^ 0xEE0E612C`), `Leb128`.
  - `Transport/` — the DRDP codec, layered exactly as the findings:
    outer datagram frame (`[src_ep][dst_ep]` + per-message flag headers + CRC
    trailer) → segment (flags byte, acks, segment seq) → message
    (type/size/seq/refnum + refnum XOR-delta) → retransmit/ack/close machinery.
    **Endianness:** quantized movement fields big-endian, all other scalars
    little-endian.
  - `Session/` — establishment (NewInstance/InstanceID keyed by (addr,
    InstanceID)) + the `LoadBotJoin` bypass (send join, await the emu's spawn
    ack). No login / char-select / world-entry.
  - `Movement/` — the 41-byte channel-0x40 record builder, the quantizer
    (`round((v-min)/(max-min) · 2^(8n))`, BE, zero-range component omitted), the
    range table, and a **world-bounded wander** that queries an `IMovementRegion`
    for valid next positions and never leaves it. Region impls: a bounded-box
    P0 stepping-stone, then a **collision/navmesh-backed** region derived from the
    client's own per-world `.esf` collision (see World placement). The region is
    supplied per bot in `BotConfig`, so the core stays world-agnostic.
  - `Bot/` — `BotClient` (public API + `Tick(nowMs)`), `BotConfig`, `BotState`,
    metrics/events, and `IBotBehavior` (the pluggable per-tick behavior seam).
    `MovementBehavior` is the only P0 implementation; combat/cast/chat plug in
    later over the same transport + session.
- **`EqoaLoadClient.Harness`** (console app): single-bot runner + the capture-diff
  conformance tool.
- **`EqoaLoadClient.Tests`** (xUnit): codec round-trips + assertions against the
  concrete byte vectors in the findings.

The emu fleet layer references `EqoaLoadClient.Core` directly and drives bots via
the `BotClient` API + a shared `Tick` scheduler.

## The bot lifecycle (state machine)

1. **Establishment** — send an outer datagram with `NewInstance | HasInstance`
   and a minted `InstanceID`; the server keys the session on (addr, InstanceID).
   Endpoint ids: send `dst_ep = 0xFFFE` (wildcard) until the server's id is
   learned. Seeds: segment seq = 1, per-channel message seq = 1, RTO interval =
   1000 ms.
2. **LoadBotJoin (login bypass)** — instead of the retail auth / login /
   char-select / world-entry flow, send one bespoke `LoadBotJoin` opcode as a
   normal reliable control message on channel `0xfb` (transport still fully
   exercised): `{ u32 botIndex, u16 zoneId, s32 x, s32 y, s32 z, u8 classId,
   u8 level }`. The emu injects a **ceremony-free but fully combat/cast-capable**
   entity keyed to the DRDP session — no DB account, no inventory ceremony, no
   char-select, no `0x000D`/`0x0014` handshake — and begins accepting the bot's
   channel-0x40 movement. `classId`/`level` are carried so the same join
   provisions an entity later behaviors (combat, casting) can drive without
   reworking the join. Opcode number assigned by the emu's registry.
3. **Movement loop** — every ~100 ms (server-set), wander **within the bot's
   assigned world region** (`IMovementRegion`) and emit the 41-byte channel-0x40
   record: update counter, quantized position (BE) + heading + a plausible
   Y-delta; velocity/accel vectors may be zero for a wander bot. Refnum XOR-delta
   is applied by the message layer against channel-0x40 history.
4. **Ack emission** — the segment flags byte carries flag `0x01`/`0x02`
   (cumulative segment + control-message acks) and `0x10` (per-channel game acks)
   per the emit contract. **Prompt single-acks are the health lever** (finding
   `drdp-retransmit-cadence`): acking promptly resets the peer's retransmit
   interval and arms fast-retransmit. An un-acking bot is dead-peer-reaped in
   60 s — non-negotiable.
5. **Clean logout** — app opcode `0x9b0`, then the DRDP FIN (outer
   `ResetConnection | HasInstance` + InstanceID; best-effort, per
   `drdp-close-fin-wire-shape`).

## World placement & distribution

Movement must stay bound to a real world. There are several — **Tunaria** (huge),
**Rathe**, **Odus**, **LavaStorm**, **Plane of Sky**, **Secrets** — of very
different sizes, so the fleet runs a large bot count on Tunaria and smaller sets
on the others. This splits across the two layers:

- **Bot core (this repo) is world-agnostic.** Each bot is handed an
  `IMovementRegion` in `BotConfig` and wanders only within it. The core never
  encodes a position outside its region.
- **Authoritative bound = the client's own collision data.** EQOA movement is
  **client-authoritative** — the server trusts the position the client sends,
  which is precisely why the *client* keeps itself bounded to the world geometry
  using the per-world collision files in the ISO (`Tunaria.esf`, `Rathe.esf`,
  `Odus.esf`, `LavaStorm.esf`, and the Plane of Sky / Secrets equivalents). The
  bot must self-bound the same way, or it will stream positions inside walls / off
  cliffs — unrealistic load that also skews the server's LOS/liquid checks. So
  the faithful `IMovementRegion` is backed by that same `.esf` collision (via the
  existing `EQOAEmu.ZoneExtractor` / `EQOA_NavmeshBuilder` pipeline). The
  bounded-box region is only a P0 stepping-stone to get the wire working; the
  collision/navmesh region is the real one and lands within the movement
  milestone.
- **Fleet (emu session) owns distribution:** the world roster, the per-world bot
  counts (Tunaria-heavy), and each bot's spawn region — derived from the same
  world data.

**Clustering matters more than spreading for the load test.** Bots spread thin
across a world have no neighbors, so the per-tick proximity / C9 path barely
fires. The fleet should *cluster* bots (via the `LoadBotJoin` spawn-cluster
field) into proximity hotspots per world: per-world counts set the scale,
clustering sets whether the load is real. Bot positions are plausibly in-world by
construction, so the server's LOS / liquid checks (`EQOAEmu.LosAndInLiquidTest`)
exercise meaningfully rather than rejecting/correcting.

## Conformance harness

Built now, run on request. Given a PCSX2 capture (raw UDP hex dump or pcap of a
real client session), it:

1. Parses the capture into ordered client→server datagrams.
2. Replays the session's inputs through the bot's encoder.
3. Diffs emitted datagrams **field-by-field** against the capture, normalizing
   per-session values (InstanceID, seq counters, timestamps, CRC-covered ranges)
   so only *semantic* divergences surface.
4. Reports the first divergence with a decoded field breakdown (which layer,
   which field, expected vs got).

This is the P1 oracle. P0 relies on functional validation + unit byte-vectors.

## Testing strategy

- **Unit (P0):** `Crc32`/`Leb128` against known vectors; quantizer round-trip
  within 1 LSB; the 41-byte channel-0x40 record byte-for-byte against the finding's
  live example; segment/outer-frame encode↔decode symmetry; XOR-delta
  encode/reconstruct.
- **Functional (P0):** one bot against a local emu — completes the lifecycle,
  wanders, survives ≥ 2× the 60 s reaper window, logs out cleanly. Assert via the
  emu's session/world state (coordination with the server session).
- **Conformance (P1):** the capture-diff harness against a provided PCSX2 capture.

## Scaling (300 → 600 → 1000+)

The core shape does not change. Scale is absorbed by: (a) the external `Tick`
scheduler in the fleet, (b) the zero-alloc hot path, (c) **fleet
self-instrumentation** — the fleet watches its own CPU headroom and tick-latency
so "server buckled" is distinguishable from "fleet buckled" (the load test is
only meaningful while bots are cheaper than the server's per-client cost), and a
starved tick loop must not silently self-reap and read as a server failure.
Escape hatch if one process can't drive the target N on the same box as the
PCSX2 clients: **shard bots across processes** — a fleet-layer decision, no core
reshape, because every bot lives behind `BotClient` + `Tick`.

## Open dependencies

- **`LoadBotJoin` opcode (bot↔emu contract).** Login/auth/char-select are
  bypassed, so the single join message is a *joint definition*, not an RE task:
  the bot emits `LoadBotJoin { u32 botIndex, u16 zoneId, s32 x, s32 y, s32 z,
  u8 classId, u8 level }` on channel `0xfb`; the emu injects a ceremony-free,
  combat/cast-capable entity keyed to the session and optionally replies with the
  assigned entity id. The emu session builds the matching handler. The exact
  opcode number and any extra placement fields (heading, spawn-cluster so bots
  *concentrate* into proximity hotspots, model/race id) are finalized on the
  bridge before P0. This removes the retail-login RE dependency from the movement
  milestone entirely. `zoneId` identifies the world/zone (Tunaria, Rathe, Odus,
  LavaStorm, Plane of Sky, Secrets).
- **Which inbound message type triggers the server→client RLE path**
  (`drdp_segment_parse`) — only relevant if the bot must *decode* an RLE'd inbound
  message; movement-only inbound is acks + position/spawn, so likely not on the
  P0 path. Confirmed as needed.

## Extensibility (future behaviors)

Movement is behavior #1, not the architecture. The `Tick(now)` loop dispatches to
a set of active `IBotBehavior`s over the shared transport + session + ack
foundation, so combat, spell casting, chat fan-out, grouping, and inventory (the
fleet proposal's later scenarios) plug in as additional behaviors **without
touching** the transport, session, or the `LoadBotJoin` provisioning. The join
already provisions a combat/cast-capable entity (class + level), so those
behaviors need only a new app-layer opcode encoder + a behavior class — not a new
entry path. This is why bypass beats seeded accounts even long-term: the entity
is real and capable, just created without the login ceremony. **P0 implements
only `MovementBehavior`;** the seam is designed now, the behaviors are built
later.

## Out of scope (later, per the fleet proposal)

Combat (mob camps, melee/procs/Lua), chat fan-out (/say //shout/group), grouping,
inventory/vendor, LFG/who, login/logout churn, and turbo/loss/latency knobs.
These layer on top of the movement baseline via the `IBotBehavior` seam after the
capacity curve exists. The fleet runner/ladder/metrics and the 300-account DB
seed are the **emu session's** deliverables, not this core's.
