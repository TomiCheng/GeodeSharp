# GeodeSharp — Implementation Progress

> Living progress tracker. Update at the start and end of each phase / sub-phase.
> `CLAUDE.md` is the unchanging plan; this file is the changing state.
> [PORTING.md](PORTING.md) is the cppcache ↔ C# class mapping table (finer-grained per-class status).
>
> **New session / phase handoff:** read this file before exploring the codebase.

---

## Status at a glance

| Phase | Status | 詳細 |
|---|---|---|
| Phase 1 — MVP (Connect / CRUD / Bulk / Query / Pool / Locator / Failover) | done | [PROGRESS1.md](PROGRESS1.md) |
| Phase 2 — 自訂物件 / HA / 訂閱 / 進階查詢 | next | [PROGRESS2.md](PROGRESS2.md) |
| Phase 3 — Security + Function execution | pending | — |
| Phase 4 — Performance + Partitioning | pending | — |
| Phase 5 — Code hygiene / pruning | queued | (PROGRESS.md 下方) |
| DI surface reshape (`IGeodeCacheFactory` + extensions) | planned, not started | — |

**Entry point for the next session:** Phase 2 walking skeleton. Phase 1 MVP
is closed out (Connect / CRUD / Bulk / Query / Pool-Locator-Failover); Phase
1.5 polish (`PoolStatistics` last 7 catalogue fields, `TcrPoolEndPoint`
migration, Auth-trio throw sites, TCCM dead-code, options-tree pruning)
deferred to Phase 5 / pre-release audit. Focus shifts to **sketching the
Phase 2+ feature surface** (subscription / CQ, HA / redundancy, PDX custom
objects, delta propagation, security, function execution, PR single-hop)
as top-level NIE stubs end-to-end before drilling into details — walking
skeleton first, polish later.

---

## Feature roadmap

### Phase 1 (MVP — production-ready client)

- Connect
- Single-key CRUD (Put / Get / Remove / ContainsKey)
- Bulk ops (PutAll / GetAll / RemoveAll)
- Clear
- Invalidate
- Region convenience queries (ExistsValue / SelectValue)
- Built-in type serialization (incl. collections: List, Dictionary, array, HashSet)
- OQL queries (`SELECT *`, `SELECT COUNT(*)`, and multi-column projection
  `SELECT field1, field2` — pulled forward from Phase 2 because the
  result-decoder `StructSet` branch shares a code path with `ResultSet`;
  deferring would leave a half-built switch that silently returns garbage on
  projection queries)
- Connection pool
- Locator discovery
- Server failover / automatic reconnect

### Phase 2 (custom objects + advanced query)

- Custom-object serialization (PDX)
- Interop with the Java client
- PdxInstance (read fields without full deserialization)
- Continuous Query (server-push subscription)
- Transactions (Begin / Commit / Rollback)

### Phase 3 (security + compute)

- Authentication (username/password, custom auth provider)
- TLS / mTLS
- Function execution (server-side)

### Phase 4 (performance + partitioning)

- Delta propagation (ship only changed fields)
- Partition resolver (custom colocation)

### Phase 5 (code hygiene / pruning)

Dead-code removal, options-tree pruning, deferred test work — items
that are non-functional cleanup, scoped after the feature phases.

### Not implementing

- **cache.xml** — replaced by `appsettings.json` + `IOptions<T>`.
- **Sub-regions** — Geode itself discourages them.
- **Sync API** — async only.
- **Cache listener / loader / writer** — niche; easier server-side in Java.
- **Region expiration / eviction** — managed server-side; the client stays out.

---

## Wire protocol summary

### Frame layout (all big-endian)

```
+------------------+------------------+------------------+------------------+
| MessageType i32  | MessageLength i32| NumParts i32     | TransactionId i32|
+------------------+------------------+------------------+------------------+
| EarlyAck u8      |                                                        |
+------------------+--------------------------------------------------------+
| Part 1, Part 2, ... NumParts parts                                        |
+----------------------------------------------------------------------------+

Part:
+------------------+----------+--------+-------------+
| PartLength i32   | IsObject | Type   | Payload     |
|                  | u8       | u8     | (PartLen B) |
+------------------+----------+--------+-------------+
```

### Handshake (the easiest place to slip)

Handshake **does not** use the standard frame format — it is an ad-hoc byte
sequence. Translate `cppcache/src/TcrConnection.cpp::sendHandshakeForServer`
byte-by-byte. **Do not write it from memory.**

### MessageType

Canonical list is the `Geode.Client.Protocol.MessageType` enum at
`src/Geode.Client/Protocol/MessageType.cs` (mirror of cppcache
`cppcache/src/TcrMessage.hpp`). Which values land in which sub-phase is tracked
by the phase sections below.

---

## Configuration

cppcache uses two files: `.ini` (`SystemProperties`) and `cache.xml` (region /
pool declarations, parsed by Xerces). **We drop both and use the .NET
`IOptions<T>` pattern** — `appsettings.json` + `IConfiguration` bind straight
to record / class options. **No `cache.xml`. No `.ini`.**

### Options policy

1. **Mirror first, prune later.** When porting a cppcache config knob, **copy
   every property over** (one C# property per cppcache key, defaults matching
   cppcache constants). Pruning happens once and late — roughly end of Phase
   1.5 or before the first NuGet release — when we audit which properties
   have a code path that actually reads them. Don't judge at porting time
   which property "looks unused"; the cppcache audit window stays open until
   the .NET pool design settles.

2. **Document semantics on the property, not in side notes.** Each options
   property's XML doc records what was learned from reading cppcache: which
   file consumes it, what it actually drives (e.g. `SO_SNDBUF`, expiry-task
   interval, per-endpoint cap), whether it is pool-level / connection-level
   / endpoint-level, and any platform quirks (e.g. `#ifdef __linux`). The doc
   is the audit trail — six months later, someone reviewing the property
   should not need to re-read cppcache to understand it.

3. **Don't invent JSON schema for things not yet implemented.** Concrete JSON
   shape is decided per phase against cppcache `SystemProperties` semantics;
   don't write a target schema for code that doesn't exist yet.

---

## Public API sketch (DI-first)

```csharp
// Registration
builder.Services.AddGeodeClient(builder.Configuration.GetSection("Geode"));

// Use
public class OrderService(IGeodeCache cache)
{
    private readonly IRegion<string, byte[]> _orders = cache.GetRegion<string, byte[]>("orders");
    public Task SaveAsync(string id, byte[] payload, CancellationToken ct)
        => _orders.PutAsync(id, payload, ct);
}
```

Current interface shape lives in `src/Geode.Client/` — `IGeodeCache`, `IRegion`
/ `IRegion<TKey,TValue>` (typed overlay with `where TKey : IEquatable<TKey>`),
`IQueryService`, `IQuery<T>`. Source is the source of truth; no parallel
interface list is maintained here.

**Important:** the MVP does not support `cache.xml` and does not create
regions. A DBA pre-creates regions with `gfsh` (`gfsh create region
--name=test --type=REPLICATE`); the client is a proxy.

---

## In progress

_Phase 1 is closed out. Phase 2+ walking skeleton work has not started yet
— next session begins by sketching the Phase 2 feature surface as
top-level NIE stubs._

---

## Completed

Phase 1 (MVP 階段) 詳細紀錄已搬到 [PROGRESS1.md](PROGRESS1.md)。

---

### Phase 0 — DI + entry interfaces

Entry interfaces: `IGeodeCache` / `IRegion<TKey,TValue>` /
`IQueryService` / `IQuery<T>` / `IGeodeCacheFactory`. `GeodeException`
(BCL exceptions for transport / API misuse; `GeodeException` for Geode
protocol failures). `GeodeClientOptions` + sub-options (full cppcache
mirror, schema to be pruned later). `AddGeodeClient` three overloads
(host config / external IConfiguration / Action delegate) × named &
unnamed. `IGeodeCacheFactory` + `GeodeCacheFactory` (per-cache
`AsyncServiceScope`, `Lazy<T>` race guard, cascading async dispose).
`GeodeCache.EnsureInitializedAsync` uses
`Lazy<Task>(ExecutionAndPublication)`. 130 unit tests green, 0 build
warnings.

Carry-over (intentional, not gaps): `IRegion` / `IQueryService` /
`IQuery` are empty shells (methods filled in Phase 1.2 / 1.4);
`GeodeClientOptions` is the full cppcache `SystemProperties` mirror
(incl. `LogOptions` / `StatisticsOptions` / `HeapOptions` /
`CacheOptions` / `ThreadPoolSize` / `EnableChunkHandlerThread`) per
the "mirror then prune" policy, pruning happens late Phase 1.5 /
pre-release; each sub-options class needs xmldoc filled in with
cppcache origin (consumer file / semantics / platform constraints) per
CLAUDE.md "Document semantics on the property"; no `AuthOptions` yet
(Phase 3 security).

---

## Phase 2 — 自訂物件、HA、訂閱、進階查詢

Phase 2 詳細請見 [PROGRESS2.md](PROGRESS2.md)。

範圍:PDX 自訂物件、訂閱通道 / Continuous Query、HA / 冗餘、
Transactions。從 Phase 1.5 推來的 Locator follow-ons
(`getEndpointForNewCallBackConn` 等)也在那邊。

## Phase 5 — Code hygiene / pruning

Non-functional cleanup queued behind the feature phases. None block
shipping; they reduce surface area / dead code once the feature work
is mature enough to know what survives.

### Options-tree prune (audit complete; details in commit history)

- **`HeapOptions`** — server-side concept (`heap-lru-limit` /
  `heap-lru-delta` / `tombstone-timeout`), no client analogue;
  referenced only by `GeodeClientOptions.Heap` + clone/validate.
- **`PoolOptions` system-properties layer (5 fields)** —
  `ConnectionPoolSize` (per-EP cap not implemented),
  `ConnectWaitTimeout` (Linux EPIPE workaround irrelevant under .NET
  async sockets), `MaxSocketBufferSize` (never applied to socket),
  `ShuffleEndpoints` (our DM uses `Random.Shared.Next` at
  construction), `BucketWaitTimeout` (Phase 4+ PR routing).
- **`GeodeClientOptions` root (2 fields)** — `ThreadPoolSize` and
  `EnableChunkHandlerThread` (xmldoc admits both are "very likely
  no-ops" under .NET; the latter has one stale TODO marker in
  `ThinClientBaseDM.cs:66`).
- **`CachePoolOptions` per-pool layer (4 fields)** —
  `SocketBufferSize` (duplicate of `PoolOptions.MaxSocketBufferSize`),
  `Subscription{AckInterval,MessageTrackingTimeout,Redundancy}`
  (Phase 2+ subscription — re-add when CQ work starts).
- **`CacheOptions` cache layer (2 fields)** — `RedundancyLevel`
  (Phase 2+ subscription redundancy), `Version` (pinned `"1.0"`,
  never validated).
- **Kept (consumer scheduled for a known phase, do NOT prune):**
  `CachePoolOptions.MultiuserAuthentication` (Phase 3,
  `_isMultiUserMode` already reads it), `SubscriptionEnabled`
  (Phase 2+ `ThinClientPoolHADM` factory selector),
  `ThreadLocalConnections` (Phase 1.5 sticky factory selector),
  `PingInterval` (deliberately nullable for the two-layer
  `xmlPool.PingInterval ?? options.Pool.PingInterval` fallback).

### Lifecycle dead-code

- **TCCM dead-code removal** — the inventory is done but nothing has
  moved. Drop the 6 NIE methods + their dead fields, simplify
  `InitAsync` (drop the `isPool` parameter), rewrite the class XML doc
  to reflect the real role ("endpoint registry + durable flag holder").
  ~80 lines deleted, ~10 changed.
- **Release TCCM endpoint refs**
  (`ConnManager.RemoveRefToTcrEndpointAsync`) — currently piggybacks on
  cache-scope dispose cascade.

### Deferred tests

- **`PutInQueueAsync` tests** — `_isDestroyed` guard
  (cppcache `ConnectionQueue::put` `closed_` branch,
  `ConnectionQueue.hpp:62-67`) is implemented but untested. Happy path
  is implicitly covered by every back-to-back op in
  `CacheConnectionIntegrationTests` / `RegionCrudIntegrationTests`
  (conn enqueued by op #1, picked up by op #2). The destroyed-guard
  itself is structurally unreachable from public API
  (`SendRequestToEndpointAsync` rejects on `_isDestroyed != 0` at the
  top) — only fires in a race window mid-`SendRequestToEndpointAsync`.
  Deterministic test needs either (a) wire-response orchestration in
  integration test to pause `SendAsync` while `DestroyAsync` races, or
  (b) visibility relaxation + DI-tree scaffolding + spy on a `sealed`
  `TcrConnection`. Both cost-ineffective relative to the 5-line guard.
  Revisit when `PoolDisconnects` Meter or socket-leak tooling lands
  (then the guard would have an observable counterpart). Source: inline
  comment in `ThinClientPoolDM.PutInQueueAsync` flags this deferral.
