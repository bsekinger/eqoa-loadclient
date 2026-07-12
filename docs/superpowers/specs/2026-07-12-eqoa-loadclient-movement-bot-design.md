# eqoa-loadclient — movement-only bot client core (design)

**Date:** 2026-07-12
**Status:** approved (brainstorming) → pending implementation plan
**Repo:** `github.com/bsekinger/eqoa-loadclient`

## Goal

A C# (.NET 9) fleet of lightweight virtual EQOA clients that speak real DRDP to
the EQOAEmu server, to load-test it and baseline capacity before the
transport-decoupling work. This spec covers the **movement-only** milestone: a
single bot that logs in, enters world, wanders, acks correctly, and logs out —
built as a class-library **core** the emu's .NET fleet layer hosts in-process.

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
    InstanceID)) + the login→char-select→world-entry state machine.
  - `Movement/` — the 41-byte channel-0x40 record builder, the quantizer
    (`round((v-min)/(max-min) · 2^(8n))`, BE, zero-range component omitted), the
    range table, and a simple wander behavior.
  - `Bot/` — `BotClient` (public API), `BotConfig`, `BotState`, metrics/events.
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
2. **Login** — on reliable control channel `0xfb`: hello opcode `0x0000` (+u32
   `0x25`), identify opcode `0x0904`. *(Exact payloads: see Open Dependencies.)*
3. **Char-select** — soft-ack the server's messages so the cumulative control-ack
   reaches `0x02`; trigger character selection.
4. **World-entry** — receive `0x000D` (world/character dump; ack and discard),
   which gates the client; reply with the bare u16 `0x0014` ("character in world")
   on channel `0xfb`.
5. **Movement loop** — every ~100 ms (server-set), emit the 41-byte channel-0x40
   record: update counter, quantized position (BE) + heading + a plausible
   Y-delta; velocity/accel vectors may be zero for a wander bot. Refnum XOR-delta
   is applied by the message layer against channel-0x40 history.
6. **Ack emission** — the segment flags byte carries flag `0x01`/`0x02`
   (cumulative segment + control-message acks) and `0x10` (per-channel game acks)
   per the emit contract. **Prompt single-acks are the health lever** (finding
   `drdp-retransmit-cadence`): acking promptly resets the peer's retransmit
   interval and arms fast-retransmit. An un-acking bot is dead-peer-reaped in
   60 s — non-negotiable.
7. **Clean logout** — app opcode `0x9b0`, then the DRDP FIN (outer
   `ResetConnection | HasInstance` + InstanceID; best-effort, per
   `drdp-close-fin-wire-shape`).

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

- **Login/char-select opcode payloads.** The skeleton is known (`FUN_012b9bb8`:
  drdp connect → `0x0000`+u32 `0x25` → `0x0904` → char-select → `0x000D`→`0x0014`),
  but the exact byte contents of `0x0000`/`0x0904`/char-select are not fully
  pinned. **P0 approach:** scope login to satisfy the **emu's** login handler
  (quick contract with the server session), since the bot targets the emu.
  **Follow-up:** RE the retail payloads so the bot also validates login against a
  real capture. Tracked as a separate finding request on the bridge if needed.
- **Which inbound message type triggers the server→client RLE path**
  (`drdp_segment_parse`) — only relevant if the bot must *decode* an RLE'd inbound
  message; movement-only inbound is acks + position/spawn, so likely not on the
  P0 path. Confirmed as needed.

## Out of scope (later, per the fleet proposal)

Combat (mob camps, melee/procs/Lua), chat fan-out (/say //shout/group), grouping,
inventory/vendor, LFG/who, login/logout churn, and turbo/loss/latency knobs.
These layer on top of the movement baseline after the capacity curve exists. The
fleet runner/ladder/metrics and the 300-account DB seed are the **emu session's**
deliverables, not this core's.
