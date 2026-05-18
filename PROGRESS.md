# GeodeSharp — Implementation Progress

> Living progress tracker. Update at the start and end of each phase / sub-phase.
> `CLAUDE.md` is the unchanging plan; this file is the changing state.
> [PORTING.md](PORTING.md) is the cppcache ↔ C# class mapping table (finer-grained per-class status).
>
> **New session / phase handoff:** read this file before exploring the codebase.

---

## Status at a glance

| Phase | Status |
|---|---|
| Phase 0 — DI + entry interfaces | done |
| Phase 1.1 — Single server connection | done |
| Phase 1.2 — Single-key CRUD | done |
| Phase 1.3 — Bulk + management ops | done |
| Phase 1.4 — OQL query | done |
| Phase 1.5 — Connection management | in progress |
| DI surface reshape (`IGeodeCacheFactory` + extensions) | planned, not started |
| Phase 2+ — Custom objects, security, performance, partitioning | pending |

**Entry point for the next session:** Phase 1.5 — Connection management. Current
focus is `PoolOptions` mirror-then-prune review, dead-code removal in
`TcrConnectionManager`, and finishing the failover / health-monitor /
`PoolStatistics` catalogue work.

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

### Phase 1.5 — Connection management

#### To do

- **Server endpoint health monitoring.**
- **Fresh-conn race proper fix** (pool warmup / readiness probe) —
  tests currently use `FreshConnectionSettleDelay = 3s` to dodge it
  (memory `geode-fresh-conn-race.md`).
- **`PoolStatistics` catalogue progression** — 7 of 27 fields wired
  (`locatorRequests` / `locatorResponses` collapsed into
  `ClientConnectionRequestTime`, `PoolConnections` gauge,
  `LoadConditioningConnects` / `LoadConditioningDisconnects` /
  `IdleDisconnects` / `PoolConnects` / `PoolDisconnects` counters).
  `PoolDisconnects` exists but isn't wired into every close site. The
  remaining 20 land per catalogue order (`clientOps*` on the
  send-sync-request path, `connectionWait*` in the conn queue, ...).
  `_pingTickCount` / `_pingSuccessCount` to be folded the same way
  `_updateLocatorTickCount` was (Histogram + `MeterCapture`).
- **Auth-trio real throw sites** —
  `AuthenticationFailedException` / `AuthenticationRequiredException` /
  `NotAuthorizedException` classes exist but nothing throws them
  (Phase 3 security). Handshake step 9 (`acceptanceCode != REPLY_OK`
  branch) will be the throw site once we map cppcache `AUTH_REQUIRED` /
  `AUTH_FAILED`.

#### Done

- **Server failover verification test landed** —
  `ServerFailoverIntegrationTests.Ops_succeed_via_failover_after_one_server_is_stopped`
  drives the retry frame end-to-end: locator-mode pool against the
  2-locator + 3-server fixture, sentinel Put/Get to confirm baseline
  health, `gfsh stop server --name=srv1` via the fixture's
  `GfshAsync`, then 30 Put + 30 Get round trips that must all
  succeed via failover to srv2 / srv3 — any unhandled socket /
  connection-refused that escapes `SendSyncRequestCoreAsync`'s
  catch block surfaces as a test failure here. `try / finally`
  restarts srv1 so downstream tests in the same collection-fixture
  run see the full topology. Side fix: `--hostname-for-clients=localhost`
  re-added to both locators in `GeodeFixture` (was temporarily removed
  while diagnosing the locator-request ordinal-width bug fixed in
  d7f1b3d), so `LocatorListResponse` peer entries stay host-reachable
  and future locator-mode tests don't each need to set
  `UpdateLocatorListInterval = TimeSpan.Zero` as a workaround.

- **Connection pool cap — design decided as two-layer** —
  pool-wide (`CachePoolOptions.MaxConnections`) AND per-endpoint
  (`PoolOptions.ConnectionPoolSize`, default 5), mirroring cppcache.
  Per-endpoint cap landed via `TcrEndpoint._slots`
  (`SemaphoreSlim?`, null = unlimited — our re-interpretation of
  cppcache's `0` to drop the "lazy single conn" mode at
  `TcrEndpoint.cpp:869-883`) with `AcquireSlotAsync` / `ReleaseSlot`
  helpers. `TcrConnection.OwnsEndpointSlot` flag carries the slot
  reservation across the conn lifetime; `DisposeAsync` auto-releases.
  `ThinClientPoolDM.CreatePoolConnectionAsync` and
  `CreatePoolConnectionToAEndPointAsync` both dual-acquire; per-EP
  cap behaviour differs by call site — endpoint-pinned throws
  `AllConnectionsInUseException`, failover-loop blacklists and tries
  the next server. `ConnectionPoolSize` resurrected from the Phase 5
  prune list with full xmldoc + `Validate >= 0`.

- **`LogOptions` + `StatisticsOptions` deleted** — first slice of the
  `PoolOptions` mirror-then-prune execution. Both classes had been
  flagged "deletion shortlist" in their own xmldoc: `LogOptions`
  (`log-file` / `log-level` / `log-file-size-limit` /
  `log-disk-space-limit` — superseded by `ILogger<T>` per CLAUDE.md)
  and `StatisticsOptions` (`statistic-*` archive — superseded by
  `EventCounters` / `Meter`). `GeodeClientOptions.Log` /
  `.Statistics` properties + their ctor / clone / validate references
  removed; corresponding test classes in `PrimitiveOptionsTests` and
  the Clone-NotSame assertions in `GeodeClientOptionsTests` trimmed.
  `HeapOptions` still pending (held back until we decide whether
  Phase 4 `tombstone-timeout` needs a stub).

- **DM-level retry frame in `SendSyncRequestCoreAsync`** — cppcache
  `ThinClientPoolDM.cpp:1294-1322` ported as Steps A-G. **A**: loop
  state (`retriesLeft` / `retryAllEpsOnce` / `excludeServers` /
  `firstTry` / `lastError`); `attemptFailover=false` overrides pool
  retry config and pins to a single attempt. **B**: `while
  (retryAllEpsOnce || retriesLeft-- > 0)` wrapping Steps 1-3.
  **C**: `TcrMessage.UpdateHeaderForRetry()` on resend (new method
  sets EarlyAck retry bit `0x4` via `with`-clone; cppcache
  `TcrMessage.cpp:805-809`). **D**: query-family timeout
  short-circuit (cppcache:1312-1322 skip-list shared with
  `IsQueryFamilyType`, renamed from `ShouldApplyReadTimeout`).
  **E**: `IsRetryableTransportError` first-cut taxonomy (`IOException`
  / `SocketException` / `TimeoutException` / non-caller-cancelled
  `OperationCanceledException`); full `GfErrType` port still
  deferred. **F**: `excludeServers.Add(failed location)` quarantines
  the endpoint (cppcache:1453); `attemptedLocation` hoisted out of
  the try so catch can see it. **G**: post-loop
  `throw lastError ?? GeodeException("retries exhausted")`. Pool
  `CachePoolOptions.ReadTimeout` linked onto caller ct via
  `CreateLinkedTokenSource` + `CancelAfter` for non-query/PutAll/CQ
  types (cppcache:1281-1292; query-family carry their own wire-level
  timeout via TcrMessageBuilder). `ReadTimeout` itself tightened from
  `TimeSpan?` to `TimeSpan = 10s` (cppcache `DEFAULT_READ_TIMEOUT`).

- **`SendRequestToEndpointAsync` / `SendSyncRequestAsync` overload
  merges** — both public overload pairs (chunked / non-chunked)
  collapsed to private cores (`SendRequestToEndpointCoreAsync` /
  `SendSyncRequestCoreAsync`) taking `TcrChunkedResult?`; public
  methods become thin delegating shells. cppcache itself is one
  function per layer (chunked vs. non-chunked configured on the reply
  object, not by overload); our two bodies were ~95% duplicated.
  Phase 3 auth-retry, Phase 1.5 retry frame, Phase 4 PR metadata
  refresh TODOs only need writing once now.

- **Phase 3 auth-path call sites stubbed + wired in
  `SendRequestToEndpointCoreAsync`** — three NIE stubs added against
  their cppcache counterparts: `TcrMessage.IsUserInitiativeOps`
  (`TcrMessage.cpp:98`), `TcrMessage.GetException`
  (`TcrMessage.cpp:213`), `ThinClientBaseDM.IsAuthRequireException`
  (`ThinClientBaseDM.cpp:374`). Two call sites threaded through the
  endpoint-pinned send: `(IsSecurityOn || IsMultiUserMode) &&
  IsUserInitiativeOps(request)` before send (cppcache:1912);
  `IsSecurityOn && reply.MessageType == Exception &&
  IsAuthRequireException(reply.GetException())` after (cppcache:1975).
  Guards short-circuit in Phase 1.x defaults (security off → never
  enters NIE); when a user opts into auth config the NIE clearly
  signals the missing Phase 3 work. Phase 3 step list for the
  unauth + outer-retry loop lives inline at the throw site.
  **`ThinClientPoolDM` exposes `IsMultiUserMode` / `IsSecurityOn`**
  as `override` properties off the existing `_isMultiUserMode` /
  `_isSecurityOn` backing fields (previously private and disconnected
  from the base virtuals — so the guards above always saw `false`).

- **`RemoveEPFromMetadataIfError` wired into the
  `SendRequestToEndpointCoreAsync` catch** — closes the
  cppcache:1555 / 1968 parity gap noted in the catch block. Filters
  on `Exception is IOException or TimeoutException` (cppcache
  `GF_IOERR || GF_TIMEOUT`) before dispatching to
  `_clientMetadataService?.RemoveBucketServerLocation(endpoint.Name)`.
  New `ClientMetadataService.RemoveBucketServerLocation` as a Phase 4
  walking-skeleton no-op (matches `StartAsync` / `StopAsync`
  pattern — not NIE because it fires on every IO failure path; real
  body lands with Phase 4 PR single-hop).

- **Pool subclass split + lifecycle leaf wiring** —
  `ThinClientPoolDM` opened for inheritance (`sealed` removed,
  `_stickyManager` promoted to `protected`,
  `CleanStickyConnectionsAsync` + `RemoveCallbackConnectionAsync` become
  `protected virtual`, both bodies revert to no-op to mirror cppcache
  base `{}`). New `ThinClientPoolStickyDM` overrides
  `CleanStickyConnectionsAsync` to dispatch
  `_stickyManager.CleanStaleStickyConnectionAsync(ct)` (cppcache
  `ThinClientPoolStickyDM.cpp:134-140`); new `ThinClientPoolHADM`
  overrides `RemoveCallbackConnectionAsync` with the Phase 2+ HA
  redundancy-manager TODO. Pool factory still always picks the base
  `ThinClientPoolDM`; subclass selection by
  `ThreadLocalConnections` / `SubscriptionEnabled` is a downstream
  factory wiring task. New leaf
  `ThinClientStickyManager.CleanStaleStickyConnectionAsync` no-op stub
  + Phase 6 TODO.

- **`ConnManageLoopAsync` + sub-loop hardening** —
  `CleanStickyConnectionsAsync` slot wired between clean-stale and
  restore-min (cppcache order). Tick LogTrace
  (`queue size = {Q}, _poolSize = {P}`) replaces missing cppcache LOGFINE.
  Catch-all `LogWarning` replaces silent swallow (cppcache L568-574
  parity). Stale "10s initial delay" / step-order claim in XML doc
  fixed. **`CleanStaleConnectionsAsync` split** into
  `ClassifyStaleConns` (snapshot scan, sync) +
  `ReplaceOrDeleteStaleConnsAsync` (close/rotate, async) with shared
  `SafeCloseAsync` local helper (cppcache `try { GF_SAFE_DELETE } catch {}`
  parity — one bad CloseAsync no longer aborts the sweep). Phase 2+ HA
  subscription-queue guard surfaced as inline TODO at the classification
  site. **`RestoreMinConnectionsAsync`** gains entry/exit LogDebug
  (cppcache L528/L550-551), the `limit = 2 * min` retry cap (cppcache
  L531/L538 — guards against the race where `_poolSize` never catches up),
  and a new `_stats.MinPoolSizeConnect()` tick per restored conn.

- **PoolStatistics catalogue progression** — three new instruments wired
  to their cppcache counterparts: `MinPoolSizeConnects` Counter
  (cppcache `minPoolSizeConnects` `PoolStatistics.cpp:59-62`,
  fired by `RestoreMinConnectionsAsync`), `PingTicks` /
  `PingSuccesses` Counters (no cppcache parity — our own ping-loop
  liveness signals, replacing the test-only `pool.PingTickCount` /
  `pool.PingSuccessCount` properties via `MeterCapture` in
  `CacheConnectionIntegrationTests.PingLoop_pings_endpoint_against_real_server`).
  Whole file converted from `//` comments to XML doc per
  `xmldoc-concise-style` (class summary + per-instrument summary +
  per-method one-liner; `<see cref>` cross-refs).

- **`CachePoolOptions` sentinel-nullable conversions** —
  `RetryAttempts` `int?` → `int = 3` (cppcache `DEFAULT_RETRY_ATTEMPTS
  = -1` sentinel → 3, surfaced directly), validator rejects negative,
  `ThinClientLocatorHelper` drops its `<= 0 → 3` fallback so `0` now
  means "no retries" end-to-end (footgun fixed). `PrSingleHopEnabled`
  `bool?` → `bool = true` (cppcache `DEFAULT_PR_SINGLE_HOP_ENABLED =
  true`), consumer drops `?? true`. Both XML docs expanded with cppcache
  ref + default/min/max. **Deleted** `StatisticInterval` (dead mirror —
  cppcache `PoolStatsSampler` not ported, option had zero consumers).

- **`_opConnections` data structure swap (`Channel<T>` → `LinkedList<T>` +
  `Lock`)** — Phase 1.5 multi-endpoint prep. Direct mirror of cppcache
  `queue_` + `mutex_` (`ThinClientPoolDM.cpp:2156`). Picked over Channel
  because per-endpoint ops (`getFromEP`, `removeEPConnections`,
  `getNoGetLock`) need iterate-and-erase-by-predicate, which Channel
  can't express without drain/repush gymnastics; consumers always
  `TryRead` (caller opens a new conn on empty), so Channel's
  wake-on-write signal was never load-bearing. 11 sites translated
  1:1, semantics preserved — Phase 1.1 single-endpoint shortcut still
  takes head (`First` + `RemoveFirst`):
  - `GetFromEPAsync` / `PutInQueueAsync` — simple `TryRead` / `WriteAsync`
    swap. `PutInQueueAsync` collapses to sync (returns
    `ValueTask.CompletedTask`).
  - `RestoreMinConnectionsAsync` — single `WriteAsync` → `AddLast`.
  - `DestroyAsync` Step 5a — `TryComplete` + drain becomes
    snapshot-and-clear under lock, `CloseAsync` awaits outside the lock
    so close I/O isn't held under it.
  - `CleanStaleConnectionsAsync` — `Reader.Count` → `lock + Count`;
    destructive `TryRead` → `lock + First/RemoveFirst`; 3 push-back
    sites → `lock + AddLast`. Drain/repush gymnastics preserved this
    round; can collapse to in-place node walk in a later refactor.
  - `GetFromEPAsync`'s Step A-D roadmap rewritten to match (in-place
    `node.Next` walk + `Remove(node)`, FIFO preserved exactly — Step B
    "re-enqueue" becomes n/a). Body still the Phase 1.1 shortcut; real
    per-endpoint scan is the next round.
  - Tests: 715/715 unit + 99/99 integration (6 expected skips) green —
    behaviour-preserving refactor verified.

- **Options family rename** — `CacheXml*` → `Cache*`, folder
  `Options/CacheXml/` → `Options/Cache/`. `GeodeClientOptions.CacheXml`
  property → `Cache`, JSON path moves with it. `CacheXmlHostPort` →
  `CacheHostPortOptions` (also added the `Options` suffix to match the
  family). Reason: the project never parses XML, the prefix was stale
  heritage and misleading. (commit `61ca0a1`)

- **Drop `GeodeClientOptions.CacheFile`** — mirror of cppcache
  `cache-xml-file` SystemProperty, zero consumers. XML doc had marked it
  "included only to make its removal auditable"; audit window closed with
  the rename. (same commit)

- **`<client-cache endpoints=>` entry mirror + synthesis** —
  `CacheOptions.Endpoints` changed from `string` to
  `List<CacheHostPortOptions>` (typed shape, cppcache CSV semantics).
  Validator enforces `Endpoints` and `Pools` mutually exclusive (mirrors
  cppcache `PoolAttributes::addLocator/addServer`'s
  `IllegalArgumentException("Cannot add both locators and servers to a pool")`,
  hoisted up to the root). New `Cache.ResolvePoolsToBuild(CacheOptions)`
  internal static pure function: non-empty `Endpoints` synthesises a single
  `CachePoolOptions { Name = "default", Servers = Endpoints.Clone() }`,
  other properties take `CachePoolOptions` defaults. `PoolManager.DefaultPool`
  uses the "first `AddPool` wins" rule so the synthesised pool naturally
  becomes the default. Function does not mutate `_options.Cache` (when
  `Create` has no `action` it forwards the live `baseOptions`; mutation
  would poison the `IOptionsMonitor` cached instance across caches).
  Design basis: cppcache `CacheXmlParser.cpp:553-560` does the same
  `<client-cache endpoints=>` → `addServer` conversion, but a misplaced
  `if (poolFactory_)` guard silently drops the request. Our "modernisation"
  is to fix that bug.
  - Tests: `CacheResolvePoolsToBuildTests` (6 cases, pure-function
    behaviour) + `CacheEndpointsConfigIntegrationTests` (2 cases,
    end-to-end DefaultPool synthesis + Put/Get round-trip against a real
    server).
  - Decided: we do **not** implement the cppcache `TcrConnectionManager`
    non-pool background-worker path, but we **do** accept the cppcache
    top-level `<client-cache endpoints=>` entry and normalise it
    internally to a default pool. "Don't support non-pool runtime" and
    "do support the non-pool config entry" are two different decisions;
    now they're separated.

- **TCCM inventory (decided, not yet acted on)** — under pool-only, only
  the endpoint registry (`_endpoints` + `AddRefToTcrEndpointAsync`) is in
  use; the remaining 6 NIE methods and many dead fields are non-pool / HA
  mirror shell. **Cleanup deferred** to be done together with the next
  pool / failover work in this phase.

- **`CachePoolOptions.UpdateLocatorListInterval` tightened** — `TimeSpan?`
  → `TimeSpan` defaulting to 5s (cppcache
  `PoolFactory::DEFAULT_UPDATE_LOCATOR_LIST_INTERVAL`). Validator now
  requires `>= 0`, mirroring cppcache `PoolFactory.cpp:150`'s
  `IllegalArgumentException("timeout must be positive.")`. Initially
  promoted to `PoolOptions` as a global default but reverted: cppcache has
  no SystemProperties entry for it, and inventing one would add a config
  knob with no upstream parallel. Settled as `ThinClientPoolDM` inline
  `?? 5s` (rule: "don't invent config knobs"; cppcache having a `DEFAULT_*`
  constant but no `.ini`/XSD entry is not a license to expose a property).

- **Locator helper — Steps A–E in place**, locator-mode pool end-to-end
  operational:
  - **A — shell + wire-up** — `ServerLocation` record (`{Host, Port}`,
    mirror of cppcache `ServerLocation::toData`), `ThinClientLocatorHelper`
    shell, `ThinClientPoolDM._locatorHelper` typed (was `object?`),
    `ScheduleUpdateLocatorLoop` builds it via `ActivatorUtilities`. The
    `CacheHostPortOptions` ↔ wire-layer `ServerLocation` conversion
    happens at this boundary.
  - **B — wire codec** — `LocatorListRequest` / `LocatorListResponse` /
    `ClientConnectionRequest` / `ClientConnectionResponse` records.
    DSFid is centralised in
    [Protocol/DSFid.cs](src/Geode.Client/Protocol/DSFid.cs) (was
    duplicated). `BigEndianBinaryReader.ReadString` (NIE stub from
    1.3.c) got its `CacheableNullString` / `CacheableASCIIString` /
    `CacheableString` branches filled in.
  - **C — `LocatorConnection`** — one-shot TCP + `NoDelay` + three-step
    clean close (`FlushAsync` → `Socket.Shutdown(Both)` to send FIN →
    dispose stream/client, each step in its own try/catch so later steps
    still run). Distinct from `TcrConnection`: no handshake, no 17-byte
    header, no TX id.
  - **D — `UpdateLocatorsAsync` real impl** — snapshot + shuffle → for
    each locator run `BuildLocatorListRequestFrame` →
    `LocatorConnection.SendAsync` → grow-buffer + parse-on-grow
    (`EndOfStreamException` = need more bytes, until decode succeeds) →
    merge returned locators with client-known (preserving locators the
    client knows but the server did not return, matching
    `ThinClientLocatorHelper.cpp:298-303`) → atomic swap under lock.
    SSL reject (first byte = 21) raises `NotSupportedException` (Phase 3
    TLS).
  - **E — `GetEndpointForNewFwdConnAsync` + `SelectEndpointAsync`
    locator branch** — cycle locators mod size up to `_connectionRetries`
    (cppcache `getConnRetries`: `RetryAttempts ?? 3`).
    `response.ServerFound == false` sets a `locatorFound` flag
    distinguishing "locator unreachable" vs "locator reachable but
    cluster empty". `ThinClientPoolDM.SelectEndpointAsync` split into
    `SelectEndpointFromLocatorAsync` / `SelectEndpointFromStaticServerList`
    helpers; dispatcher body collapses to three if/throw lines.
  - **Shared scaffolding** — helper-internal `BuildRequestFrame(DSFid,
    writeBody)` / `TrySendAsync<T>(..., DSFid expected, bodyDecoder)` /
    `ReadEnvelope(reader, expectedDsfid)`, so both send paths share one
    send/receive/decode skeleton.
  - **Roadmap lives in source, not in conversation** —
    `ThinClientLocatorHelper.UpdateLocatorsAsync` carries an A–E roadmap
    comment in the body (per CLAUDE.md rule 11: read cppcache + leave a
    step list in the C# stub before implementing).
  - Tests:
    - `LocatorWireCodecTests` — 11 unit cases, byte-fixture against the
      four wire records; caught a hand-arithmetic mistake (`0x99D4` vs
      `0x9DD4`) that justifies the byte fixtures' existence.
    - `LocatorModeIntegrationTests` against a real fixture locator:
      `Pool_with_locator_initialises_against_real_locator` (init path)
      + `UpdateLocatorList_loop_ticks_against_real_locator` (**the key
      one** — proves wire bytes actually reach the locator,
      `LocatorListResponse` decodes in the live client, tick ≥ 2).
      Put/Get round-trip is `Skip`ped because Testcontainers maps a port
      that doesn't match the hostname-for-clients the locator returns;
      fixing needs `--hostname-for-clients=<host>` +
      `WithPortBinding(40404, 40404)`.

- **CLAUDE.md gained implementation principles #10 / #11 / #12** — don't
  auto-run tests; before implementing, read the C++ and leave a step list
  in the C# stub; when the user says "commit", commit without
  re-confirming a draft message.

- **`PoolStatistics` observability foundation** — mirror of cppcache
  `PoolStatistics.{hpp,cpp}` (cppcache class is `PoolStats`; 27-field
  catalogue documented inline in
  [`PoolStatistics.cs`](src/Geode.Client/Internal/PoolStatistics.cs)
  against `PoolStatistics.cpp:34-122` so new stats can be ticked off).
  - **Bucket 3** (thin wrapper) not Bucket 2 (port the whole `Statistics`
    subsystem): BCL `System.Diagnostics.Metrics` (`Meter` / `Counter` /
    `Histogram`) + `ActivitySource` already cover the OTel abstraction;
    no need to re-implement cppcache `StatisticsFactory` /
    `StatisticDescriptor` / `AtomicStatistics`. The `.gfs` archive (a
    cppcache `PoolStatsSampler` VSD-specific binary format) is the wrong
    semantics for the .NET ecosystem; OTel / Prometheus is the right
    export channel.
  - **Meter and ActivitySource share name `"Geode.Client.Pool"` +
    `AssemblyVersion`** (from
    `typeof(PoolStatistics).Assembly.GetName().Version`; MinVer
    auto-injects). Picked `GetName().Version` over
    `AssemblyInformationalVersion` for simplicity now.
  - **`LocatorListRequest` and `ClientConnectionRequest` are two
    separate Histograms and two ActivitySource spans**, mirroring the
    two wire RPCs. Tried a merged-with-outcome-tag design and reverted:
    the two RPCs have different use / frequency / failure cost
    (`ClientConnectionRequest` failure blocks a user op;
    `LocatorListRequest` failure only ages the list), so dashboards /
    SLO alerts should see them separately. Span name is low-cardinality
    operation identity — easier to facet in trace UIs.
  - **Histogram is `<double>` + `unit: "s"`** rather than `<long>` +
    `"ns"` (cppcache parity): Prometheus default histogram buckets are
    seconds-scale so nanosecond values collapse into the `+Inf` bucket.
    PromQL / Grafana convention is the `_seconds` suffix. The cppcache
    `int64_t ns` origin is noted in a code comment.
  - **`ThinClientPoolDM._stats` field-init uses
    `ActivatorUtilities.CreateInstance<PoolStatistics>(serviceProvider,
    xmlPool.Name)`** so Logger and friends added later flow in via DI;
    pool name passes as runtime arg.
  - **`_updateLocatorTickCount` removed** —
    `LocatorListRequestTime.Count` replaces it 1:1 (every
    `UpdateLocatorsLocalAsync` records the Histogram in `finally`,
    including exception paths). `_pingTickCount` / `_pingSuccessCount`
    to follow in the same pattern.
  - **`MeterCapture` test helper**
    ([tests/Geode.Client.IntegrationTests/MeterCapture.cs](tests/Geode.Client.IntegrationTests/MeterCapture.cs))
    — `MeterListener` wrapper, takes both `<long>` and `<double>`
    instruments, exposes `.Count` + `.Sum`. Replaces the ad-hoc
    Interlocked counter approach so we don't need an internal snapshot
    property running parallel to the Meter.
  - Tests: `LocatorModeIntegrationTests` both cases switch to
    `MeterCapture`:
    - `Pool_with_locator_initialises_against_real_locator` → asserts
      `ClientConnectionRequestTime.Count >= 1` (`RestoreMinConnections
      → SelectEndpointFromLocator` path)
    - `UpdateLocatorList_loop_ticks_against_real_locator` → asserts
      `LocatorListRequestTime.Count >= 2` (background update loop, 1s
      initial delay + 200ms interval)

- **`PoolConnections` `ObservableGauge` (catalogue gauge #1)** — mirror
  of cppcache `poolConnections` IntGauge (`PoolStatistics.cpp:51-52`),
  i.e. the .NET equivalent of cppcache `m_poolSize` push-mode reporting.
  - **Pull (`ObservableGauge`) not push (`UpDownCounter`)**: `_poolSize`
    is mutated in 4 places (`CreatePoolConnectionAsync` step 4 increment,
    two conn-destroy decrements, warm-up increment); push would need
    every call site instrumented and is easy to miss. Pull reads when
    the listener asks, no instrumentation-gap risk.
  - **Static registry + shared instrument** — `PoolStatistics` keeps a
    static `ConcurrentDictionary<string, Func<int>>
    _poolConnectionsReaders` of per-pool readers; a single static
    `ObservableGauge<int>` callback iterates the dict and emits one
    `Measurement<int>` per pool (with `poolName` tag). Multi-pool
    naturally differentiates by tag, no per-pool instrument needed.
  - **"Reader is registered later" entry points** —
    `SetPoolConnectionsReader(Func<int>)` / `ClearPoolConnectionsReader()`.
    C# field-init can't capture `this`, so `ThinClientPoolDM._stats`
    field-init can't pass `() => Volatile.Read(ref _poolSize)` into the
    PoolStatistics ctor. Solved by registering in init / clearing in
    destroy:
    - `InitAsync`, right after the idempotent guard:
      `_stats.SetPoolConnectionsReader(() => Volatile.Read(ref _poolSize))`
      — gauge goes live the moment init completes.
    - `DestroyAsync` step 5c (after `_endpoints.Clear()`):
      `_stats.ClearPoolConnectionsReader()` — lets the gauge observe
      step 5a decrementing as connections drain, only then removes the
      registry entry so the static dict doesn't accumulate dead entries.
  - **`MeterCapture` extension** — added `Observe()`
    (`MeterListener.RecordObservableInstruments()` wrapper, manually
    triggers pull-instrument callbacks) and `LastValue` (last observed
    gauge value). Push-instrument `.Count` / `.Sum` unchanged.
  - Test:
    `CacheConnectionIntegrationTests.PoolConnections_gauge_reports_current_pool_size`
    (server-mode pool, MinConn 1 → `Observe()` → assert `LastValue >= 1`).
    Covers the whole wire: InitAsync register → conn-management loop
    opens connection → `_poolSize++` → MeterListener pulls reader →
    exporter sees `PoolConnections{poolName=testPool} = 1`.

- **`CleanStaleConnectionsAsync` end-to-end** (commit `373eb30`) —
  cppcache `ThinClientPoolDM::cleanStaleConnections`
  (`ThinClientPoolDM.cpp:402-~500`) fully landed: the pool
  conn-management loop runs this before `RestoreMinConnections` every
  tick, scanning the idle queue and either destroying or replacing
  connections under two reasons: load-conditioning (age >
  `LoadConditioningInterval`) or idle (unused > `IdleTimeout` AND
  `_poolSize > Min`).
  - **Step A prerequisites in place:**
    - `TcrConnection.Touch()` now fills `_lastAccessed` (monotonic
      `Stopwatch.GetTimestamp()`); call site is `PutInQueueAsync` (conn
      returns to queue).
    - `TcrConnection.IsIdle` / `HasExpired` / `UpdateCreationTime`
      helpers mirror cppcache `TcrConnection.cpp:1183/1193/1222`.
      `HasExpired` includes `_expiryTimeVariancePercentage` jitter
      (each conn rolls `RandomNumberGenerator.GetInt32(-9, 10)` in
      ctor, mirroring cppcache `:65-70`, to avoid expiry avalanche).
    - `CachePoolOptions.LoadConditioningInterval` `TimeSpan?` →
      `TimeSpan` defaulting to 5 minutes (cppcache
      `PoolFactory::DEFAULT_LOAD_CONDITIONING_INTERVAL`); validator
      rejects negative (`PoolFactory.cpp:83-86` parity). `IdleTimeout`
      validator also gained the missing negative check.
    - **`TcrConnection` member mirror** — the 13 fields in cppcache
      `TcrConnection.hpp:272-363` (`_connectionId` / `_endpointObj` /
      `_poolDM` / ...). Existing fields get real (nullable) types;
      `binary_semaphore` and the like sit as `object?` placeholders.
      `#pragma warning disable CS0169, CS0414, CS0649` wraps the
      placeholders to keep the build quiet.
  - **Step B classification** — snapshot `_opConnections.Reader.Count`,
    bound a single pass, `TryRead` and pop each conn into one of three
    buckets (HasExpired / IsIdle+poolSize>Min / keep). Introduced a
    `RemovalReason { LoadConditioning, Idle }` enum + tuple
    `List<(TcrConnection, RemovalReason)>` so Step C knows why.
    (cppcache lumps both into `incLoadCondDisconnects`; we split idle
    vs load-cond counters so the catalogue has clear semantics.)
  - **Step C destroy vs replace:**
    - `replaceCount = Min - savedConns`; `<= 0` is pure shrink (per
      reason, `IdleDisconnect` or `LoadConditioningDisconnect`).
    - `> 0` tries `CreatePoolConnectionAsync` to open a new conn →
      success + different conn → push new, destroy old, both
      `LoadConditioningDisconnect` + `LoadConditioningConnect`.
    - Open failed + `HasExpired` → destroy regardless,
      `LoadConditioningDisconnect`.
    - Open failed + not expired → `conn.UpdateCreationTime()` resets
      age + push back to queue (cppcache `:488`; without this the next
      sweep picks the same conn again).
  - **`PoolStatistics` 3 new counters** mirror cppcache `idleDisconnects`
    / `loadConditioningConnects` / `loadConditioningDisconnects`
    (`PoolStatistics.cpp:63-73`, IntCounter parity, `Counter<int>` +
    `poolName` tag).
  - **Call site** — `ConnManageLoopAsync` awaits
    `CleanStaleConnectionsAsync(ct)` before `RestoreMinConnections`,
    matching cppcache `manageConnectionsInternal` order.
  - **Drive-by refactor** — `StartBackgroundThreads` extracted ping
    setup into `SchedulePingLoop()`, mirroring `ScheduleUpdateLocatorLoop()`
    shape.
  - **`PingExtensions` folded back into `TcrConnection`** — the extension
    method `PingAsync` previously lived in
    `Protocol/Operations/PingExtensions.cs` with only two test callers;
    the extension layer wasn't earning its keep. Now an instance method
    taking ctor-injected `messageBuilder` (not pulled from
    `ServiceProvider`). The `Operations/` folder is gone.
  - Tests:
    - `CleanStaleConnections_idle_path_shrinks_pool_and_bumps_IdleDisconnects`
      — Min=0, IdleTimeout=200ms, LoadCond=10min, PingInterval=0 (ping
      disabled so it doesn't open conns in the background). After a 3s
      settle delay, `region.PutAsync` opens a conn; next sweep destroys
      it → `IdleDisconnects.Count >= 1` + `PoolConnections == 0`.
    - `CleanStaleConnections_loadCond_path_replaces_conn_and_bumps_LoadConditioning_counters`
      — Min=1, IdleTimeout=200ms, LoadCond=500ms. Wait for
      `RestoreMin`'s conn to age past LoadCond → both LoadCond counters
      ≥ 1 + `PoolConnections == 1` (replace doesn't shrink).
  - **Note:** memory `geode-fresh-conn-race.md` revalidated — under a
    cold container the server-side `ClientHealthMonitor` registration
    for a fresh conn has a 5–100ms window. User ops (e.g.
    `region.PutAsync`) hitting too early eat `RegionDestroyedException`.
    The idle-path test uses a 3s settle delay to dodge this (other
    user-op-driven tests follow the same pattern).

- **`CreatePoolConnectionAsync` failover retry + recycle hint full
  cppcache parity** (cppcache `ThinClientPoolDM.cpp:1725-1802`). Sister
  method `CreatePoolConnectionToAEndPointAsync` brought to the same state.
  - **Step A — prerequisites:**
    - **Exception taxonomy (8 `GeodeException` subclasses)** mapping the
      8 Geode-runtime entries among cppcache `ExceptionTypes.hpp`'s 58 —
      `GeodeException` (base), `NoAvailableLocatorsException`,
      `CacheServerException` (renamed from `ServerException` to match
      cppcache), `AuthenticationFailedException`,
      `AuthenticationRequiredException`, `NotAuthorizedException`,
      `NotConnectedException`, `AllConnectionsInUseException`. Covers
      the complete cppcache `isFatalClientError` set (the auth trio +
      locator failure) plus a fatal-other and a transient representative.
      Each class xmldoc notes the `GfErrType::XXX` source and whether
      pool failover treats it as fatal-client or transient.
    - **`ConnectTimeout` chain wired** — `PoolOptions.ConnectTimeout`
      (already existed, mirror of cppcache `SystemProperties::connect-timeout`,
      default 59s, validator `>= 0`) flows from pool into
      `TcrEndpoint.CreateNewConnectionAsync` into
      `TcrConnection.ConnectAsync`. `ConnectAsync` uses
      `CreateLinkedTokenSource` + `CancelAfter` to bound both TCP
      connect and handshake under the same budget, matching cppcache
      `initTcrConnection`'s two-leg propagation. Signature now
      `ConnectAsync(host, port, TimeSpan? connectTimeout, ct)`; test
      call sites use named-arg `cancellationToken: ct` to fix positional
      shift.
    - **`CreatePoolConnectionAsync` signature grew** —
      `(HashSet<DnsEndPoint> excludeServers, TcrConnection? currentServer
      = null, CancellationToken ct = default)`. cppcache uses
      `ServerLocation` set; we use `DnsEndPoint`, the pool-layer
      `_endpoints` registry key (`ServerLocation` only crosses the
      locator boundary). `HashSet<>` not `ISet<>` (CA1859: private
      method, no abstraction value, slightly faster `Contains`/`Add`).
    - **`SelectEndpointAsync(HashSet<DnsEndPoint> excludeServers, ct)`** —
      locator branch converts the set to `ServerLocation` list at the
      helper boundary (helper already accepted this parameter,
      previously hardcoded `[]`); static-server branch round-robins
      skipping excluded. **All excluded → throws
      `NotConnectedException`** (first real thrower of
      `NotConnectedException`).
    - **`MaxConnections` cap via `SemaphoreSlim` to fix the race** —
      cppcache serialises check+increment with a mutex; we use a
      `private readonly SemaphoreSlim? _capSlots` (Max=null → semaphore
      = null, all `?.` short-circuit). `Wait(0, ct)` is fail-fast,
      throws `AllConnectionsInUseException`. Design lets us switch to
      `WaitAsync(FreeConnectionTimeout, ct)` later to mirror the
      cppcache setter — semantics fit naturally.
  - **Step B — failover retry loop:** `while (true)` body: select
    endpoint → AddEP → open conn → on failure classify via catch filter
    (`AuthenticationFailedException`/`AuthenticationRequiredException`/
    `NotAuthorizedException`/`NoAvailableLocatorsException` → propagate
    fatal-client; everything else → blacklist + `continue`). Three
    exits: `return conn` on success / `return null` when fully excluded
    (SelectEndpoint threw `NotConnectedException`, we catch back) /
    fatal-client propagates.
  - **Step C — Exception classification without a dedicated predicate
    method** — cppcache's `isFatalClientError` / `isFatalError` static
    helpers inline directly into `catch ... when (ex is X or Y or ...)`
    filters. C# types replace enum dispatch, IDE clicks through, no
    intermediate. cppcache's "lastFatalError memory + return that error
    at the end" mechanism is not needed in .NET — exceptions handle it
    natively (the failure *is* the exception; keep it by throwing, wrap
    it via inner).
  - **Step D — caller updates:**
    - `RestoreMinConnectionsAsync` opens a fresh
      `new HashSet<DnsEndPoint>()` + null currentServer per iter.
    - `CleanStaleConnectionsAsync` Step C replace path passes **empty**
      excludeServers (cppcache parity — locators naturally spread,
      recycle hint handles the same-server case) + `conn` as
      currentServer.
    - `SendRequestToEndpoint` family pass empty + null (op-layer outer
      retry is the `sendSyncRequest` scope — still on the to-do list).
  - **Recycle hint** (cppcache `L1760-1765`) implemented:
    `TcrConnection.Endpoint` property
    `internal TcrEndpoint? Endpoint { get; set; }` promoted from the
    mirror block's `_endpointObj` placeholder;
    `TcrEndpoint.CreateNewConnectionAsync` sets `conn.Endpoint = this`
    once handshake succeeds. CleanStale replace: if SelectEndpoint picks
    the same endpoint → `currentServer.UpdateCreationTime()` + `return
    currentServer` (slot and conn retained, handshake not wasted).
    Under single-server config this fires every time (expected,
    cppcache parity).
  - **Slot management with `try/finally` + `releaseSlot` flag** — slot
    is released by default (`releaseSlot = true`); only kept when we
    successfully opened a brand-new conn and are returning it (slot
    transfers to the conn, released when it's closed). All close sites
    (5 × `Interlocked.Decrement(ref _poolSize)`) add
    `_capSlots?.Release()`; `DestroyAsync` step 4 adds
    `_capSlots?.Dispose()`. Recycle / failure / exception / OCE all
    flow through `finally` — no leak path.
  - **`CreatePoolConnectionToAEndPointAsync`** picked up the same
    pattern — slot reservation + stats wiring (`PoolConnect` +
    conditional `LoadConditioningConnect`), clearing two TODOs.
  - **`PoolStatistics.PoolConnect` / `PoolDisconnect` counters** mirror
    cppcache `connects` / `disconnects` IntCounter
    (`PoolStatistics.cpp:53-58`, catalogue fields #6/#7). Connect wired
    into both `CreatePoolConnectionAsync` and
    `CreatePoolConnectionToAEndPointAsync` success paths; disconnect not
    yet wired into every close site (next round).
  - **PORTING.md gained §3 Exception hierarchy** — all 58 cppcache
    exceptions classified into bucket-1 (BCL replacement) and bucket-2
    (`GeodeException` subclass), each tagged with the BCL / our class
    it maps to + phase + status. Current state: 8 / 40 bucket-2 built.
  - **xmldoc cleanup** — `PoolOptions.ConnectTimeout`,
    `CachePoolOptions.MaxConnections` rewritten user-facing (one-line
    summary of what + remarks line of default + edge case), matching
    `MinConnections` / `LoadConditioningInterval`.
  - Test:
    `CleanStaleConnections_loadCond_path_bumps_LoadConditioningDisconnects`
    switched to `Min=0` + `LoadCond=50ms` + `PingInterval=0` + 3s
    settle + Put. The recycle hint makes replace a no-op under
    single-server, so the test moved from "replace both counters ≥ 1"
    to "pure-shrink only `LoadConditioningDisconnects` bumps". 99/105
    integration tests pass (other 6 are pre-existing skips for N/A
    scenarios).

---

### DI surface reshape — `IGeodeCacheFactory` + `GeodeClientExtensions` (planned)

**Nature**: a revisit of the Phase 0 design, not a new phase. Scope is
`src/Geode.Client/IGeodeCacheFactory.cs` +
`src/Geode.Client/Services/GeodeCacheFactory.cs` +
`src/Geode.Client/GeodeClientExtensions.cs` + every options class (add
`ICloneable` + copy ctor) + corresponding tests.

#### Background

The Phase 0 design: `AddGeodeClient` 3 overloads (unnamed + optional `name`),
`IGeodeCacheFactory.Get(name)` lazy-builds, the DI container exposes both
`IGeodeCache` (unnamed alias) and `[FromKeyedServices(name)] IGeodeCache`
(keyed). Works for Phase 0 but accumulates problems:

- `Get(name)` lazy-build conflicts with the natural "missing → throw"
  expectation.
- Keyed-singleton instances go stale once a future `RemoveAsync` lands.
- No cacheName / configName decoupling, so multi-cluster sharing a config
  or runtime overrides aren't possible.
- `IGeodeCacheFactory` only has `Get` — no enumeration, removal, or
  explicit build entry.

#### Key forks in the discussion

1. **`Get` missing behaviour** — null / bool / `KeyNotFoundException`.
   Settled: `Get` throws `KeyNotFoundException`, `TryGet` returns bool.
   Aligns with `IServiceProvider.GetRequiredService` / `GetService`.
2. **Should every Cache go through the factory?** Briefly converged on
   "factory only, drop direct `IGeodeCache` injection". Reverted to
   two-layer: 95% users have one cluster + EF Core's dual-injection
   pattern is the prior art. Simple users inject `IGeodeCache` directly;
   advanced users use `IGeodeCacheFactory`.
3. **Manual or auto Create?** Manual. `AddGeodeClient` only registers
   config and the `IGeodeCache` injection point; `factory.Create()` must
   be called at startup. `IGeodeCache` injection before `Create` →
   `KeyNotFoundException`, fail-fast not silent magic. Production and
   test behave the same.
4. **cacheName / configName decoupling** added to `Create`. One config
   feeds multiple caches (read/write split, tenant isolation). `Get` /
   `RemoveAsync` only know cacheName.
5. **`Action<sp, opts>` cascade semantics** — `Create`'s `action`: look
   up configName → Clone → action mutates the clone → re-run validator
   → build cache from the clone. Original config untouched.
6. **DeepClone approach** — rejected `ICloneable` (MS guidance) and
   JSON round-trip (future non-JSON properties). Picked B: each options
   class adds its own `DeepClone()` method, no interface. **⚠️ Reverted,
   see "Subsequent revision".**
7. **`AddGeodeClient` / `AddGeodeFactory` split** — two methods × 3
   overloads each. `AddGeodeClient` is always unnamed and registers the
   `IGeodeCache` alias; `AddGeodeFactory` puts `name` last (default
   `""`), only adds to the factory, no `IGeodeCache` alias.
8. **Validation moves into `GeodeClientOptions`** — add `Validate(string?
   name = null)` returning `ValidateOptionsResult`.
   `GeodeClientOptionsValidator` shrinks to a one-line
   `opts.Validate(name)` forward. Benefits: (a) `Create` after DeepClone
   + action does `clone.Validate(configName)` directly, no
   `IValidateOptions<T>` lookup from sp; (b) cohesion — options
   validates itself; (c) tests can bypass DI. Sub-options classes add
   `Validate()` the same way; root recurses.

#### Final shape

```csharp
public static class GeodeClientExtensions
{
    public static IServiceCollection AddGeodeClient(this IServiceCollection services);
    public static IServiceCollection AddGeodeClient(this IServiceCollection services, IConfiguration cfg);
    public static IServiceCollection AddGeodeClient(this IServiceCollection services, Action<GeodeClientOptions> configure);

    public static IServiceCollection AddGeodeFactory(this IServiceCollection services, string name = "");
    public static IServiceCollection AddGeodeFactory(this IServiceCollection services, IConfiguration cfg, string name = "");
    public static IServiceCollection AddGeodeFactory(this IServiceCollection services, Action<GeodeClientOptions> configure, string name = "");
}

public interface IGeodeCacheFactory
{
    IGeodeCache Get(string cacheName = "");                                   // KeyNotFoundException if missing
    bool TryGet(string cacheName, [NotNullWhen(true)] out IGeodeCache? cache);
    IGeodeCache Create(                                                       // InvalidOperationException if cacheName exists
        string cacheName = "",
        string configName = "",
        Action<IServiceProvider, GeodeClientOptions>? action = null);
    IReadOnlyCollection<string> CacheNames { get; }
    ValueTask<bool> RemoveAsync(string cacheName);
}
```

Behaviour contract:

- 95% case: `AddGeodeClient(cfg)` → `factory.Create()` at startup → call
  sites inject `IGeodeCache`.
- 5% case: `AddGeodeFactory(cfg, "legacy")` → `factory.Create("legacy",
  "legacy")` → `factory.Get("legacy")`.
- DI keyed `[FromKeyedServices]` injection is not supported at all
  (avoids the `RemoveAsync` stale-instance landmine).

#### Withdrawn proposals

- Validator tightening `Cache == null` — kept nullable, revisit once the
  manual-build path lands.
- `Register` / `Unregister` runtime options (via
  `IOptionsMonitorCache<T>.TryAdd`) — `Create(action)` covers it.
- `RegisteredNames` / `IsRegistered` — dropped the "ask if a config is
  registered" notion.
- `GeodeClientRegistry` sidecar — not needed.
- `ICloneable` — see Subsequent revision.
- `IDeepCloneable<T>` interface — over-abstracted, simplified.
- `[FromKeyedServices]` keyed injection — everything via factory.
- `AddGeodeClient` auto-Create (hosted service) — manual, keeps prod /
  test identical.
- `GetOrCreate(name, action)` — silent-ignore-on-second-call landmine.
- `IGeodeCache?` Get (nullable return) — throw instead, don't force
  callers to handle null.

#### Subsequent revision — back to `ICloneable` (2026-05-16)

Originally rejected `ICloneable` per "MS guidance + deep/shallow
ambiguity". After implementing once, the lack of a common marker
interface felt off — there was no way to see "this class is designed to
be copyable" at a glance. Back to `ICloneable` + a strongly-typed public
`Clone()` + copy ctor:

```csharp
public class XxxOptions : ICloneable
{
    public XxxOptions() { }                          // IConfiguration binding
    public XxxOptions(XxxOptions other) { ... }      // member-wise, incl. nested deep clone
    public XxxOptions Clone() => new(this);
    object ICloneable.Clone() => Clone();            // explicit interface
}
```

The deep/shallow ambiguity dissolves once `Clone()`'s xmldoc says "Deep
clone via copy constructor." and every options class is consistent (all
deep). Polymorphism (`CacheLibraryOptions` ↔
`CachePersistenceManagerOptions`) uses `virtual Clone()` + covariant
override; the base only needs one explicit `ICloneable.Clone()` (virtual
dispatch reaches the subclass).

Scope: 20 options classes + 1 call site (`GeodeCacheFactory.Create`) +
11 test files.

#### Implementation order

1. List the `Cache*` nested classes; complete the options class roster.
2. Add `DeepClone()` (later renamed `Clone()`) + `Validate(name)` to
   each options class.
3. Options unit tests (per-class round-trip + mutation isolation +
   Validate positive/negative).
4. `GeodeClientOptionsValidator` shrinks to a thin wrapper forwarding to
   `opts.Validate(name)` (DI registration stays to preserve the
   `ValidateOnStart` pipeline).
5. Reshape `IGeodeCacheFactory` (5 members).
6. Reshape `GeodeCacheFactory` impl (Get/Dispose race fix via a
   `DisposeEntryAsync` helper shared with `RemoveAsync`; `Create(action)`
   calls `clone.Validate(configName)` after DeepClone + action).
7. `GeodeClientExtensions` 6 overloads + drop keyed/unnamed `IGeodeCache`
   surface beyond what's listed + rewrite xmldoc.
8. Update existing test call sites (grep `[FromKeyedServices]` and
   `IGeodeCacheFactory.Get` for blast radius).
9. New tests: Create-duplicate throws, Create+action mutation isolation,
   Create+action validator fail, Get/TryGet missing, RemoveAsync then
   re-Create same name, CacheNames snapshot behaviour.
10. Build + test green, commit.

Pause for review after each step (per memory rule).

---

## Completed

### Phase 1.4 — OQL query

#### Scope landed

- `IQueryService.NewQuery<T>(oql)` / `IQuery<T>` interface + DI wiring.
- `RemoteQueryService` + `RemoteQuery<T>` with full `ExecuteCoreAsync`
  (B1-B11): closed-guard / logs / TcrMessage build / DM send /
  server-exception handling / result projection.
- `TcrMessageBuilder.Query(34)` / `QueryWithParameters(80)` encoders.
- `ChunkedQueryResponse<T>` full decoder — C1-C12 main flow, R1-R3
  `ReadObjectPartList`, S1-S4 `SkipClass`, K1-K2 `Reset`, plus
  `ReadStructRow` / `ReadExceptionAndThrow` helpers. All three wire
  shapes handled: scalar COUNT (C3b), `CacheableObjectArray` (C11a),
  `CacheableObjectPartList` (C11b).
- **`QueryStruct` public type** (pulled forward from Phase 2) — named
  `QueryStruct` because `Struct` collides with the C# keyword.
  Implements `IReadOnlyList<object?>` + by-name indexer + `FieldNames` /
  `GetFieldIndex` / `GetFieldName`.
- **StructSet realised** — Option C: the collector assembles a
  `QueryStruct` every K values and pushes it directly, skipping the
  cppcache "flatten → outer reshape" intermediate. B10 collapses to a
  single `return`.
- **`NewQuery` type guard** — `T` must be a `SerializationRegistry`-registered
  type or `QueryStruct`, blocking bucket-2 (PDX custom types) and
  bucket-4 (ORM mapping).
- `BigEndianBinaryReader.ReadArrayLength` — Java variable-length array
  length decode (cppcache `DataInput::readArrayLength` parity).
- `TcrPartBuilder.ModifiedUtf8` + `RegionName` now delegates — OQL /
  region path encoding switched from ASCII to Modified UTF-8 body,
  matching Java `CacheServerHelper.fromUTF`. Pure-ASCII case is
  byte-identical.
- **`QueryExtensions`** — `ExecuteSingleAsync` /
  `ExecuteFirstOrDefaultAsync` / `WithParameters` /
  `WithResponseTimeout`, caller-side fluent / scalar wrappers.
- **Region convenience** `ExistsValueAsync` / `SelectValueAsync` on
  `IRegion` + typed overlay `IRegion<TKey,TValue>.SelectValueAsync`
  (typed, `new Task<TValue?>`). Implementation uses
  `ThinClientRegion.QueryAsync` private helper (mirror of cppcache
  `Region::query`). OQL string assembly: caller-provided full query
  (`^\s*(?:select|import)\b` detection) → verbatim; otherwise prepend
  `select distinct * from <FullPath> this where ` (the `this` alias
  declared in the FROM clause matches cppcache
  `ThinClientRegion.cpp:536-540`). `RegionView<TKey,TValue>` gains 3
  forwarders (`ExistsValueAsync`, typed `SelectValueAsync<TValue>` via
  adapter, explicit `IRegion.SelectValueAsync` skipping adapter).
- **`RemoteQueryService.NewQuery<object>` whitelist** — type-guard adds
  the `typeof(T) != typeof(object)` exception, formalising the cppcache
  `shared_ptr<Serializable>` (≈ `object?`) base path.
  `TypedResultAdapter.Convert<object>` was already identity
  (`IsInstanceOfType` is always true), so opening this is zero-cost.
  Region convenience uses this path internally.
- **`ProxyRemoteQueryService` stub** (Phase 3 placeholder) — mirror of
  cppcache `ProxyRemoteQueryService` (sibling of `RemoteQueryService`
  under `IQueryService`); `NewQuery<T>` is NIE, filled in Phase 3
  multi-user.

#### Deferred

- Multi-column projection / StructSet integration tests — need
  server-side PDX structured data (gfsh JSON put or Java preload).

#### Tests

- 39 unit tests: `QueryStructTests` (16) + `QueryExtensionsTests` (18)
  + `TcrMessageBuilderQueryTests` (17) +
  `TcrMessageBuilderQueryWithParametersTests` (22).
- 14 integration tests, all green: `QueryIntegrationTests` (7) covers
  `SELECT *` ResultSet, `SELECT COUNT(*)` scalar,
  `QueryWithParameters(80)` + bind values, `ExecuteSingleAsync`
  extension composition, type-mismatch → `InvalidCastException`;
  `RegionQueryConvenienceIntegrationTests` (7) covers region
  convenience.

#### Bugs caught during integration tests

**Bug 1: `TcrMessageHelper.ReadChunkPartHeader` mis-read the sign byte**
(`Protocol/TcrMessageHelper.cs:156-167`). `compId = reader.ReadByte()`
returns unsigned, but negative `DSFid` values decode wrong
(`CollectionTypeImpl = -59`'s wire byte is `0xC5`; unsigned read returns
197, which doesn't equal -59). Fix: `compId = (sbyte)reader.ReadByte()`.
Latent for GetAll / RemoveAll chunked decoders because they only use
positive DSFids (`VersionedObjectPartList = 7` etc.); query is the first
to hit a negative DSFid.

**Bug 2: `ChunkedQueryResponse` C6 / C7 / R3a too strict on short-string
DSCode** (`Services/ChunkedQueryResponse.cs`). Original only accepted
`DSCode.CacheableString(42)`, but for ASCII class / field names the
server actually sends `DSCode.CacheableASCIIString(87)`. Extracted
`ReadShortString` helper that accepts both forms — Modified UTF-8
decoding is byte-identical for ASCII, so the reader is shared. cppcache
`DataInput::readString` already dispatches on all four forms; our
previously-unimplemented huge / ASCII branches are now at least covered
for ASCII in Phase 1.4.

#### Design notes

- **Type-mismatch on `T`** (e.g. `IQuery<int>("SELECT name...")`) lets
  `InvalidCastException` bubble up naturally, same source as
  `IRegion<TKey,TValue>.GetAsync`. Integrating `TypedResultAdapter` +
  ORM mapping is deferred to the PDX phase.
- **OQL `this`** — `this` works, **but** the FROM clause must declare
  it as the region-iteration alias: `SELECT * FROM /region this WHERE
  this = ...`. Earlier integration tests wrote `SELECT * FROM /test
  WHERE this = ...` (missing alias declaration) and exploded. cppcache
  `ThinClientRegion::query` (`ThinClientRegion.cpp:536-540`) prepends
  the same way, and the region convenience `QueryAsync` helper follows
  suit.
- **Why pull projection forward** — B10's ResultSet / StructSet
  branches share the decode path with `ChunkedQueryResponse.HandleChunk`.
  fieldNames decode and row-value decode live in the same cppcache
  `readObjectPartList`. Leaving StructSet to Phase 2 would leave a
  half-built switch ("structure present but fieldNames undecoded, no
  reshape") that silently corrupts projection queries — caller writes
  `SELECT id, total` and gets a flat list with no error.
- **`NewQuery<object>` whitelist meaning** — opening `IQuery<object>`
  as public API formally accepts the "I receive whatever the wire
  decodes to, I'll handle row shape myself" path (≈ cppcache
  `shared_ptr<Serializable>` base). Post-release this can't be revoked.
  But that path is cppcache's only row-type contract anyway; `<T>` is
  the .NET type-safety sugar layered on top, so exposing `<object>` is
  what completes the picture.

---

### Phase 1.3 — Bulk + management ops

#### 1.3.0 — `IDataConverter` built-in type expansion

Phase 1.2 shipped only the `Int32` and `Boolean` converters; bulk-op
integration tests needed more representative K/V types. Landed the MVP
scalar / string / bytes converters in one pass so 1.3.a–c could build
on them.

Final: 11 Tier A converters with unit + integration tests all green
(292 unit + 17 integration). `IDataConverter` API reshaped (`DsCodes[]`
/ `GetDsCode(value)` / `Write(w, v, dsCode)` / `Read(r, dsCode)`),
matching cppcache `Serializable::getDsCode()`. `IRegion<TKey, TValue>`
gained constraint `where TKey : IEquatable<TKey>` (compile-time block
on collections / `byte[]` / POCOs without IEquatable). Drive-by fix:
`BigEndianBinaryReader.ReadArrayLen` signed/unsigned bug (Phase 1.1
latent issue — lengths 128..252 were misread as negative).

**Follow-on work:**

- **B-route server-side type verification** (commit `2854ce4`) — Put/Get
  round-trip can't prove the server actually decoded the wire bytes into
  the correct Java type (encoder/decoder bugs in the same direction
  cancel out). Added `docker exec gfsh get` to read server-side
  `Value Class` + `Value` and assert. 13 facts cover all Tier A
  converters (String gets one per DSCode variant). `GeodeFixture` gains
  a `GfshAsync` helper + container `TZ=UTC` so DateTime / java.util.Date
  print stably. **Surprise**: gfsh prints `java.util.Date` as raw
  ms-since-epoch (not `Date.toString()`), so precision is ms — stronger
  than the originally-planned second-level assertion.
  - **byte[] B-route deferred** — gfsh prints byte[] as
    `[B@<identityHash>`, nothing to assert. Phase 2 Java sidecar will
    cover it.
- **Tier B-1 primitive arrays landed** — src + unit tests done,
  integration + B-route to come. Details below.
- **Docs reshuffle** (commit `ab1d030`) — CLAUDE.md moved Bucket 1 /
  Bucket 3 tables to PORTING.md; Phase 1 sub-phase details, MessageType
  table, Public API code blocks, Phase 1.1 bootstrap prompt all removed
  (reference data goes to the right place, stale templates dropped).
  CLAUDE.md: 456 → 406 lines.

**Architecture decisions:**

`IDataConverter` reshape (mirror of cppcache
`Serializable::getDsCode()` + `Serializable::toData`):

```csharp
interface IDataConverter
{
    byte[] DsCodes { get; }                              // decode lookup; one converter may map multiple DSCodes (String: 4)
    Type ManagedType { get; }                            // encode lookup
    byte GetDsCode(object value);                        // encode-time, returns the actual DSCode based on value
    void Write(BigEndianBinaryWriter w, object value, byte dsCode);  // payload only; dsCode passed back to avoid scanning String twice
    object? Read(BigEndianBinaryReader r, byte dsCode);              // payload only; registry has read the DSCode byte
}
```

`SerializationRegistry` changes:
- `Register` loops `converter.DsCodes` and indexes every entry into
  `_byDsCode`.
- `WriteObject` does `var dsCode = converter.GetDsCode(value);
  writer.WriteByte(dsCode); converter.Write(writer, value, dsCode);`.
- `ReadObject` flow unchanged (registry still reads the DSCode byte +
  dict lookup).
- Symmetric: both Read and Write let the registry handle the DSCode
  byte, the converter handles only payload.

**Tier A — Phase 1.3.0 scope** (9 converters + one converter with
multiple DSCodes for String):

| DSCode | cppcache | CLR | Notes | State |
|---|---|---|---|---|
| 53 | `CacheableBoolean` | `bool` | | done (Phase 1.2) |
| 54 | `CacheableCharacter` | `char` | UTF-16 code unit, 2-byte BE | done |
| 55 | `CacheableByte` | `byte` | Deliberately unsigned (.NET convention); wire bit pattern interops with Java signed byte (Java -1 ↔ ours 255) | done |
| 56 | `CacheableInt16` | `short` | | done |
| 57 | `CacheableInt32` | `int` | | done (Phase 1.2) |
| 58 | `CacheableInt64` | `long` | | done |
| 59 | `CacheableFloat` | `float` | IEEE-754 BE; NaN/±∞ wire shape matches Java | done |
| 60 | `CacheableDouble` | `double` | IEEE-754 BE | done |
| 61 | `CacheableDate` | `DateTime` | 8-byte ms-since-epoch UTC. Read returns `Kind=Utc` (differs from clicache's `Local` to fix round-trip footgun); Write accepts `Utc` directly / converts `Local` via `ToUniversalTime` / **throws** `ArgumentException` on `Unspecified` (refuses to silently assume Local; clicache bug fixed). Precision truncated to ms. | done |
| 46 | `CacheableBytes` | `byte[]` | VL-encoded length + raw bytes (1/3/5 byte prefix); `null` goes via NullObj; `byte[0]` goes as DSCode 46 + length=0; **not usable as a Key** (`Array` doesn't implement `IEquatable<T>`; cppcache `CacheableArrayPrimitive` doesn't extend `CacheableKey`; compile-time blocked by `where TKey : IEquatable<TKey>`). Drive-by fix to `ReadArrayLen` signed/unsigned bug. | done |
| 42 / 87 / 88 / 89 (+69 read-only) | `CacheableString` / `…ASCIIString` / `…ASCIIStringHuge` / `…StringHuge` (+`CacheableNullString`) | `string` | One converter, four DSCodes; ASCII vs modified UTF-8 × short(u16) vs huge(u32) — but the huge UTF path uses **UTF-16 BE**, not a modified-UTF-8 huge variant (matches cppcache `writeUtf16Huge`). 69 is read-only null sentinel. `BigEndianBinaryReader.ReadJavaModifiedUtf8` upgraded from stub to real. | done |

**Tier B-1 — primitive arrays** (follow-on)

8 converters + 62 unit tests landed (unit total 323 → 385). Wire shape:
`WriteArrayLen` 1/3/5-byte VL prefix + N × element bits (primitive raw
bytes or, for `string[]`, each element's own DSCode+payload).
Integration + B-route to come.

| DSCode | cppcache | CLR | Notes |
|---|---|---|---|
| 26 | `BooleanArray` | `bool[]` | VL length + N×1 byte; decode is tolerant — any non-zero byte = true |
| 27 | `CharArray` | `char[]` | VL length + N×u16 BE (Java `char[]`, not UTF-8) |
| 47 | `CacheableInt16Array` | `short[]` | |
| 48 | `CacheableInt32Array` | `int[]` | VL boundary tests (252 / 253 / 65536) live here; other arrays share `ReadArrayLen` / `WriteArrayLen` so duplicates aren't worth it |
| 49 | `CacheableInt64Array` | `long[]` | |
| 50 | `CacheableFloatArray` | `float[]` | IEEE-754 BE, NaN / ±Infinity bit pattern preserved |
| 51 | `CacheableDoubleArray` | `double[]` | |
| 64 | `CacheableStringArray` | `string[]` | **Only** converter taking a `SerializationRegistry` ctor injection; each element re-enters `WriteObject` for full DSCode dispatch (per-element 42 / 87 / 88 / 89 / 41 all possible); `null` element goes through NullObj=41 handled by the registry one layer up; `new this(this)` is safe (converter only stores the reference, uses it on Write/Read after registry has populated). |

**Tier B-2 — collections** (core types done; Vector / LinkedHashSet
deferred)

Core architecture (built when ArrayList landed, shared by the 5
collection converters that followed):

- **`TypedResultAdapter`** (Scoped DI;
  [TypedResultAdapter.cs](src/Geode.Client/Protocol/Serialization/TypedResultAdapter.cs))
  — Java wire doesn't carry the container's element type, so every
  collection converter's `Read` returns the canonical `<object?>`-element
  container; the adapter recursively reshapes `object?` into the
  declared `TValue` at the `RegionView` boundary (`IList<int>`,
  `IList<IList<string>>`, `IDictionary<int, IList<string>>`, etc.).
  Two-pass cost is acceptable for MVP; if profiling shows a problem,
  push the hint into the converter (the public API won't break).
- **`SerializationRegistry` open-generic write fallback**
  ([SerializationRegistry.cs](src/Geode.Client/Protocol/Serialization/SerializationRegistry.cs))
  — when `_byType[runtimeType]` misses and `runtimeType.IsGenericType`,
  look up `GetGenericTypeDefinition()`. Single dict, two probes, no
  extra index. All Tier B-2 converters declare `ManagedType` as an open
  generic (`typeof(List<>)` / `typeof(HashSet<>)` /
  `typeof(Dictionary<,>)` / `typeof(LinkedList<>)` / `typeof(Stack<>)`)
  so one instance covers all closed instantiations.
- Files (architecture): the two above +
  [RegionView.cs](src/Geode.Client/Services/RegionView.cs) (adapter
  injection) + [Cache.cs](src/Geode.Client/Services/Cache.cs) (primary
  ctor takes adapter) +
  [GeodeClientExtensions.cs](src/Geode.Client/GeodeClientExtensions.cs)
  (Scoped DI registration).

Converter list:

| DSCode | cppcache | CLR | Status | Notes |
|---|---|---|---|---|
| 52 | `CacheableObjectArray` | `object[]` | done (commit `0671ae1`) | Hard-coded `"java.lang.Object"` Java class header + per-element re-entry |
| 65 | `CacheableArrayList` | `List<T>` / `IList<T>` family | done | Architecture debut (adapter + open-generic dispatch) |
| 10 | `CacheableLinkedList` | `LinkedList<T>` | done | Same wire as ArrayList (cppcache backs both with `std::vector`); adapter has its own `LinkedList<>` branch (`LinkedList<T>` doesn't implement `IList<T>`, can't share with `List<>`) |
| 66 | `CacheableHashSet` | `HashSet<T>` / `ISet<T>` / `IReadOnlySet<T>` | done | Canonical decode is `HashSet<object?>` (Java HashSet allows null elements; C++ doesn't but wire unifies); `HashSet<T>` doesn't implement non-generic ICollection, so write-side collects into a scratch list to get count |
| 67 | `CacheableHashMap` | `Dictionary<K,V>` / `IDictionary<K,V>` / `IReadOnlyDictionary<K,V>` | done | Wire key/value **interleaved** (not keys-then-values); canonical decode is `Dictionary<object, object?>`; null key rejected on read (Java HashMap allows but .NET Dictionary doesn't; explicit error beats silent death) |
| 74 | `CacheableStack` | `Stack<T>` | done | **Write reversed** to match clicache `Linq::Enumerable::Reverse(stack)` (.NET Stack iterates top→bottom, wire wants bottom→top); read pushes plain; adapter reverses again to compensate `Stack<T>(IEnumerable<T>)` ctor's push-in-iteration-order quirk |
| 71 | `CacheableVector` | — | deferred | Java's legacy thread-safe ArrayList; .NET has no equivalent (mapping to `List<T>` would collide ManagedType with ArrayList); skipped until needed |
| 73 | `CacheableLinkedHashSet` | — | deferred | .NET has no "insertion-order-preserving Set"; would need a new type (`Geode.Client.Collections.OrderedSet<T>` or similar); that's a public-API decision not a tech problem; skipped |

**Tests**: 464 unit + 18 collection-integration green. Tier B-2 direct:
79 units (ListDataConverter 9 / HashSet 8 / Dictionary 8 / LinkedList 6
/ Stack 7 / SerializationRegistry open-generic 5 / TypedResultAdapter
36); 11 round-trip + 4 B-route + 3 nested integration cases.

**gfsh quirks worth remembering** (lives in memory):

- Collections (ArrayList / LinkedList / HashSet / Stack) `Value :`
  prints `[1,2,3]` with **no spaces** (not Java standard `[1, 2, 3]`).
- HashMap prints **JSON-like** `{"42":"answer"}` — double-quotes even on
  Integer keys, not Java standard `{42=answer}`.

**Tier C — not doing or Phase 2+**: `NullObj(41)` already inlined;
`CacheableNullString(69)` goes via 41; `PdxType/PDX/PDX_ENUM` Phase 2;
`CacheableUserData*` Phase 2; `Properties(11)` Phase 3 auth;
`JavaSerializable(44)` / `DataSerializable(45)` / `Class(43)` /
`CacheableFileName(63)` / `CacheableTimeUnit(68)` rarely used, skip;
`FixedID*(1–4)` are wire-layer internal codes, not registered in
`SerializationRegistry`.

#### 1.3.a — Clear + Invalidate (non-partitioned)

State: 323 unit (292 + 31 new) + 22 integration (17 + 5 new) green
against `apachegeode/geode` real server.

- `IRegion.ClearAsync(CancellationToken)` +
  `IRegion.InvalidateAsync(object, CancellationToken)` + typed
  `IRegion<TKey,TValue>.InvalidateAsync(TKey, CancellationToken)`. No
  typed `ClearAsync` overload (no K/V parameter).
- `RegionInternal` gains 2 abstracts; `RegionView` typed forward +
  explicit `IRegion.InvalidateAsync`.
- `ClearRegion(36)` — 2 parts (regionName / eventId) or 3 (with
  callback); mirror of cppcache `TcrMessageClearRegion`
  (`TcrMessage.cpp:1644-1682`). Reply `Reply(6)` /
  `ClearRegionDataError(37)` / `Exception(2)` / else → throw. Not
  chunked.
  - `millisecondsResponseTimeout` part **not implemented** — cppcache
    `ThinClientRegion::clear` (`ThinClientRegion.cpp:777`) hardcodes
    `-1` and the normal path never sends it.
  - `localClearNoThrow` +
    `invokeCacheListenerForRegionEvent(AFTER_REGION_CLEAR)` skipped
    (Phase 2+ caching-enabled territory).
- `Invalidate(83)` — 3 parts (regionName / key / eventId) or 4 (with
  callback); mirror of cppcache `TcrMessageInvalidate`
  (`TcrMessage.cpp:1896-1932`). Reply `Reply(6)` / `Exception(2)` /
  `InvalidateError(84)` / else → throw. versionTag discarded (same as
  `RemoveAsync`).
  - One fewer pair of NullObj parts than Destroy (no `expectedOldValue`
    / `Operation` because Invalidate has no conditional overload sharing
    the ctor).
- `ThinClientRegion.ClearAsync` / `InvalidateAsync` end-to-end. Log
  severity matches cppcache `LOGFINE` / `LOGERROR`.
- Tests: `TcrMessageBuilderClearRegionTests` (15) +
  `TcrMessageBuilderInvalidateTests` (16) +
  [RegionInvalidateClearIntegrationTests](tests/Geode.Client.IntegrationTests/RegionInvalidateClearIntegrationTests.cs)
  (5: Invalidate keeps key clears value / missing-key invalidate OK /
  Put after Invalidate restores / Clear removes all keeps region /
  Clear on empty region OK).

**Not exposed**: `InvalidateRegion(55)` is server→client only; for
region-wide clearing use `ClearAsync`.

#### 1.3.b — Chunked-reply infrastructure + RemoveAll

State: 5/5 RemoveAll integration tests green; chunked-reply decoding
through the whole wire (including the `VersionTag.FromData` path for
versioned regions).

Key surface:

- `RemoveAll(109)` — 5+keys.Count parts (region / eventId / flags=0 /
  callback-or-NullObj / keyCount / N keys); mirror of cppcache
  `TcrMessageRemoveAll` (`TcrMessage.cpp:2424-2468`).
- `EventIdGenerator.NextRange(int count)` — `Interlocked.Add` reserves
  N contiguous seq ids in one shot (cppcache
  `writeEventIdPart(keys.size()-1)` parity).
- `IRegion.RemoveAllAsync(IReadOnlyCollection<object>, ct)` +
  `IRegion<TKey,TValue>.RemoveAllAsync(IReadOnlyCollection<TKey>, ct)`
  + `RegionView` typed forward (reference TKey uses covariance, value
  TKey boxes into `object[]`).
- `ThinClientRegion.RemoveAllAsync` body — build → `NextRange(N)` →
  dispatch → REPLY/RESPONSE/EXCEPTION switch.

DM / connection layer chunked path:

- `ThinClientBaseDM.SendSyncRequestAsync(TcrMessage, TcrChunkedResult,
  ...)` abstract overload.
- `ThinClientPoolDM.SendSyncRequestAsync` chunked overload —
  SelectEndpoint → AddEP → forward.
- `ThinClientPoolDM.SendRequestToEndpointAsync` chunked overload —
  borrow conn → `TcrConnection.SendRequestAsync(req, chunkedResult, ct)`
  → put-back / disconnect-on-error, shape mirrors the non-chunked
  overload.
- `TcrConnection.SendRequestAsync(req, TcrChunkedResult, ct)` —
  **inline chunked-reply loop** (cppcache `readMessageChunked` parity):
  17-byte first frame header + 5-byte subsequent chunk headers +
  last-chunk bit.
- `TcrConnection.Touch()` stub + `PutInQueueAsync` call (Phase 1.5
  `cleanStaleConnections` filled `_lastAccessed` for real).

**Key design correction**: we do **not** need cppcache's
`m_pendingReplies` + background-reader layer. cppcache's chunked path
is **inline** synchronous reads (`readMessageChunked` runs on the
sender thread); one conn serves one request at a time. The audit's
earlier judgment to build `_pendingReplies` + background reader was
wrong and was deleted after reading the real cppcache.

Chunked-result handler hierarchy:

- `TcrChunkedResult` abstract base
  ([Protocol/TcrChunkedResult.cs](src/Geode.Client/Protocol/TcrChunkedResult.cs))
  — `HandleChunk(payload, isLastChunk)` + `Reset()`. cppcache's
  `finalize` / `binary_semaphore` / `m_ex` / `m_dsmemId` slots all
  dropped (Task/await + natural exception propagation + `m_dsmemId` is
  Phase 4 territory).
- `ChunkedRemoveAllResponse`
  ([Services/ChunkedRemoveAllResponse.cs](src/Geode.Client/Services/ChunkedRemoveAllResponse.cs))
  — `Reset` mirrors cppcache 2 steps (null+size guard → clear
  versionTags); `HandleChunk` 5 steps:
  - 1: wrap payload in `BigEndianBinaryReader` (via `ActivatorUtilities`)
  - 2: `TcrMessageHelper.ReadChunkPartHeader` classifies the chunk
  - 3a: `NullObject` → return (empty reply)
  - 3b: `Object` → `new VersionedCacheableObjectPartList` + `FromData`
    + `list?.AddAll`
  - 3c: `Bytes` → read 2 bytes (single-hop metadata, real in Phase 4)
  - fallthrough: `Exception` / unknown → throw `GeodeException`
- `TcrMessageHelper.ReadChunkPartHeader` — 9-step full impl (partLen +
  isObj → early-out NullObject / Exception; DSCode branches
  JavaSerializable / NullObj / FixedIDByte+compId; mismatch → throw).
- `ChunkObjectType` enum (`NullObject` / `Object` / `Exception` /
  `Bytes`).

VersionedObjectPartList decoder (real implementation):

- `CacheableObjectPartList` base (cppcache parity; primary ctor takes
  `RegionInternal region`; 9 protected fields mirror cppcache `m_*`).
- `VersionedCacheableObjectPartList` — primary ctor `(IServiceProvider,
  SerializationRegistry, ILogger, RegionInternal)`; 7 wire fields + 4
  FLAG_* constants + `VersionTags` accessor + `Size` property (cppcache
  `size()`). `FromData` 7 steps in `lock(_responseLock)`: flags byte /
  init Values / empty message LogDebug / keys section (`_hasKeys` reads
  keys into tempKeys/ResultKeys/localKeys) / objects section
  (`hasObjects` → `ReadObjectPart` into _byteArray+Values) /
  version-tags section (`_hasTags` switch on 4 FLAG_*) / putLocal merge
  (Phase 4+ NIE). `AddAll(other)` real (cppcache 3 steps: merge keys /
  OR-in regionIsVersioned / merge versionTags). `ReadObjectPart` real
  (3 branches: exception=2 wraps `GeodeException` into `Exceptions`;
  `_serializeValues=true` raw bytes; otherwise
  `serializationRegistry.ReadObject`).
- `BigEndianBinaryReader.ReadUnsignedVL` real (Java VL unsigned u64,
  1-9 bytes, 9-byte cap throws `InvalidDataException`).
- `BigEndianBinaryReader.AdvanceCursor(int)` real / `ReadString` still
  NIE (only called by exception parts).

VersionTag + DiskVersionTag:

- `VersionTag` — primary ctor `(IServiceProvider, ILogger,
  MemberListForVersionStamp?)`; 7 fields (`_bits` / `_entryVersion` /
  `_regionVersionHighBytes` / `_regionVersionLowBytes` /
  `_internalMemId` / `_previousMemId` / `_timeStamp`) + 5 `HAS_*` /
  `VERSION_TWO_BYTES` / `DUPLICATE_MEMBER_IDS` constants + 3 `BITS_*`
  constants.
  - `FromData` 8 steps (flags / bits / skip distributedSystemId /
    entryVersion 16-or-32 / regionVersionHighBytes optional /
    regionVersionLowBytes / timeStamp VL / virtual `ReadMembers`
    dispatch).
  - `ReadMembers` 2 steps (`HAS_MEMBER_ID` →
    `ClientProxyMembershipID.ReadEssentialData` +
    `MemberListForVersionStamp.Add` → `_internalMemId`;
    `HAS_PREVIOUS_MEMBER_ID` with `DUPLICATE_MEMBER_IDS` short-circuit).
  - `ReplaceNullMemberId(memId)` real (4 lines of if-set).
- `DiskVersionTag` (`internal sealed : VersionTag`) — `ReadMembers`
  override is NIE (persistent-region DiskStoreId decoding lives in
  Phase 4+).
- `ClientProxyMembershipID` — primary ctor takes
  `SerializationRegistry`; `ReadEssentialData` real (cppcache 7-field
  wire format: array length + hostAddr bytes + hostPort + skip flag +
  vmKind + uniqueTag/vmViewIdStr (loner branch) + dsName).
- `MemberListForVersionStamp` — `Add` real (simplified: monotonic id,
  no hashKey dedup, Phase 4 finishes); `GetDsMember` real (dict lookup
  + lock).
- `DSFid` enum (25 entries incl. `VersionedObjectPartList = 7` /
  `DiskVersionTag = 2131`, 1:1 with cppcache).

Conventions adopted during this sub-phase:

- CLAUDE.md principle #9 — **cppcache wire-mirror constants use
  `SCREAMING_SNAKE_CASE`** (`FLAG_NULL_TAG` / `HAS_MEMBER_ID`);
  home-grown C# constants are `PascalCase` (`MetaTransactionId` /
  `ThreadId`). Not enforced by `.editorconfig`.
- **Internal classes inject the most-specific necessary type, not the
  interface**: `ChunkedRemoveAllResponse` takes `ThinClientRegion`;
  `VersionedCacheableObjectPartList` / `CacheableObjectPartList` take
  `RegionInternal` — sidesteps future downcast risk.
- **`ActivatorUtilities.CreateInstance` broadly adopted**:
  `ChunkedRemoveAllResponse` / `VersionedCacheableObjectPartList` /
  `VersionTag` / `DiskVersionTag` / `ClientProxyMembershipID` /
  `BigEndianBinaryReader` all build via ActivatorUtilities; DI
  dependencies auto-inject.

Tests:

- [RegionRemoveAllIntegrationTests](tests/Geode.Client.IntegrationTests/RegionRemoveAllIntegrationTests.cs)
  — 5 cases (4-key batch / mixed present+missing / empty arg / null arg
  / single-key N=1 boundary), 15s against a real server.
- [TcrMessageBuilderRemoveAllTests](tests/Geode.Client.Tests/Protocol/TcrMessageBuilderRemoveAllTests.cs)
  — 3 unit tests (header+5+N parts / per-part wire alignment / empty
  keys ArgumentException); landed during 1.3.c.

Deferred:

- `DiskVersionTag.ReadMembers` NIE (persistent region, Phase 4+) /
  `BigEndianBinaryReader.ReadString` (exception chunk, Phase 1.3.c
  GetAll might hit it) / Step 7 `putLocal` merge (`AddToLocalCache`,
  Phase 4+ client-side caching).
- Placeholder fields `_endpointMemId` / `_msg` (wrapped in `#pragma
  CS0649`) — Phase 3 auth / Phase 4 single-hop write to them.
- `MemberListForVersionStamp.Add` skips hashKey dedup — needs
  `ClientProxyMembershipID.HashKey`, Phase 4 finishes.

#### 1.3.c — PutAll + GetAll70

State: 6/6 PutAll + GetAll integration tests green; 509 unit tests
including 9 new wire-shape tests (RemoveAll 3 + PutAll 3 + GetAll 3).
Chunked reply infrastructure landed in 1.3.b; 1.3.c is mostly new wire
messages + GetAll hitting the `hasObjects=true` real path for the first
time.

Public API:

- `IRegion.PutAllAsync(IReadOnlyDictionary<object, object>,
  CancellationToken)` + typed
  `IRegion<TKey,TValue>.PutAllAsync(IReadOnlyDictionary<TKey, TValue>,
  ct)`.
- `IRegion.GetAllAsync(IReadOnlyCollection<object>, ct) →
  Task<IReadOnlyDictionary<object, object?>>` + typed
  `IRegion<TKey,TValue>.GetAllAsync →
  Task<IReadOnlyDictionary<TKey, TValue?>>`.
- `RegionInternal` gains 2 abstracts; `RegionView` typed forward +
  explicit `IRegion` impl.

Wire:

- `PutAll(56)` — 5+`map.Count`*2 parts (region / eventId /
  **skipCallbacks placeholder int=0** / flags=0 / count / N×(key,value)
  interleaved); mirror of cppcache `TcrMessagePutAll`
  (`TcrMessage.cpp:2354-2422`). Callback overload
  (`PutAllWithCallback=108`) accepts a callback parameter but throws
  `NotSupportedException` — Phase 1.3 doesn't expose it.
- `GetAll70(100)` — 3 parts (region / **inline CacheableObjectArray
  keys** / int(0) callback placeholder); mirror of cppcache
  `TcrMessageGetAll` ctor + `InitializeGetallMsg`
  (`TcrMessage.cpp:2470-2523`). Keys section inline:
  `[52][arrayLen][43][writeString "java.lang.Object"][N × WriteObject(key)]`.
  **Key point**: `writeString` itself adds a DSCode prefix (cppcache
  `DataOutput::writeString` behaviour).

Region op impl:

- `ThinClientRegion.PutAllAsync` 4-step: NextRange(N) / build /
  `ChunkedPutAllResponse` + dispatch / reply switch
  (Reply/Response/Exception/PutDataError/default).
- `ThinClientRegion.GetAllAsync` 5-step: keys materialise →
  `IReadOnlyList<object>` / build / `addToLocalCache = true &&
  (Attributes.CachingEnabled ?? false)` (mirror cppcache
  `LocalRegion::getAll_internal` hardcoded true +
  `getAllNoThrow_remote` AND with caching-enabled) →
  `ChunkedGetAllResponse` + dispatch / reply switch
  (Response/Exception/GetAllDataError/default) → return
  `chunkedResult.Values`.

Chunked-result handlers:

- `ChunkedPutAllResponse`
  ([Services/ChunkedPutAllResponse.cs](src/Geode.Client/Services/ChunkedPutAllResponse.cs))
  — structurally 1:1 with `ChunkedRemoveAllResponse`, 5-step
  HandleChunk (NullObject / Object / Bytes / Exception) + 2-step Reset.
- `ChunkedGetAllResponse`
  ([Services/ChunkedGetAllResponse.cs](src/Geode.Client/Services/ChunkedGetAllResponse.cs))
  — vs PutAll/RemoveAll, extra: (1) takes `keys: IReadOnlyList<object>`
  in ctor (chunk reply uses `Keys[index + KeysOffset]` to reverse-look
  the caller's keys); (2) `addToLocalCache: bool` ctor parameter; (3)
  `_values` / `_exceptions` / `_resultKeys` / `_keysOffset`
  accumulators; (4) HandleChunk passes the shared accumulator to
  `VCOPL.Initialize` and reads `vcObjPart.ConsumedObjectCount` after to
  advance `_keysOffset`; (5) **no NullObject / Bytes branches** —
  cppcache GetAll strictly accepts Object/Exception only; (6) `Values`
  accessor exposes `IReadOnlyDictionary<object, object?>`.

VCOPL additions:

- Added `Initialize(keys, keysOffset, values, exceptions?, resultKeys?,
  addToLocalCache)` (mirror of cppcache's 10-arg ctor role); GetAll
  chunked handler injects accumulators into the per-chunk instance.
- Added `ConsumedObjectCount` accessor (`_byteArray.Count`) — cppcache
  uses `uint32_t* m_keysOffset` shared pointer; we use post-FromData
  explicit read-back.
- Step 7 (`putLocal` merge) NIE now guarded:
  `if (hasObjects && AddToLocalCache)` — Phase 1.3 MVP has
  `AddToLocalCache` AND'd to false because `CachingEnabled = null/false`,
  so the NIE is never hit; Phase 4+ client-side caching wires it.

`addToLocalCache` flow (full cppcache mirror):

```
ThinClientRegion.GetAllAsync
  ├── const addToLocalCacheRequested = true   ← cppcache LocalRegion::getAll_internal:585 hardcoded
  └── addToLocalCache = requested && (Attributes.CachingEnabled ?? false)
                                     ↑ cppcache getAllNoThrow_remote:1100 AND
       ↓
ChunkedGetAllResponse ctor (addToLocalCache: bool, stored as field)
       ↓
VCOPL.Initialize(..., addToLocalCache)
       ↓ stored on AddToLocalCache field
VCOPL.FromData Step 7 gate: if (hasObjects && AddToLocalCache) → Phase 4+ NIE
```

Pitfalls:

**(1) `VersionTag` ActivatorUtilities ctor matching failed**

- Symptom: `A suitable constructor for type
  'Geode.Client.Protocol.VersionTag' could not be located` — GetAll
  integration test exploded on first run.
- Root cause: `ActivatorUtilities.CreateInstance<VersionTag>(sp,
  memberListForVersionStamp!)` passing null; ctor matcher can't infer
  type from null.
- Why 1.3.b RemoveAll didn't hit it: REPLICATE region defaults to
  `concurrency-checks-enabled=false`, so server replies don't ship
  version tags, and VCOPL step 6 is fully skipped. GetAll reply triggers
  `_hasTags` into step 6.
- Fix: register `MemberListForVersionStamp` as Scoped DI (per-cache,
  mirror of cppcache `CacheImpl::m_memberListForVersionStamp` instance
  scope); `NewVersionTag` signature drops the
  `MemberListForVersionStamp?` parameter and resolves purely via DI.

**(2) `IRegion<TKey, TValue?>` and value-type TValue null semantics footgun**

- Symptom: `xUnit2002: Do not use Assert.Null() on value type 'int'`
- Root cause: `TValue?` for unconstrained T is only compile-time
  nullability annotation; at runtime a value type doesn't get wrapped
  in `Nullable<T>`, so a missing key collapses to `default(int)=0` —
  indistinguishable from a real stored 0.
- Fix: `RegionView.GetAllAsync` skips null wire values → the typed dict
  doesn't contain missing keys → callers use `TryGetValue` /
  `ContainsKey` to detect (the .NET idiom); the non-typed entry retains
  cppcache parity (null stays in the dict).
- Phase 1.2 `PutAsync` / `PutAll` both guard value with
  `ArgumentNullException`, so the region literally cannot hold null. A
  null on the wire is necessarily cppcache's miss-flag-3, so skipping is
  safe.

**(3) cppcache `DataOutput::writeString` is not `writeUTF`**

- Initially assumed cppcache `writeString("java.lang.Object")` is
  `writeUTF` (u16 length + bytes, no DSCode prefix) and wrote the unit
  test against that wire shape — 5/6 pass, GetAll layout test the one
  fail.
- Reality: cppcache `DataOutput::writeString`
  ([DataOutput.hpp:264-305](D:/github/geode-native/cppcache/include/geode/DataOutput.hpp#L264))
  **prepends a DSCode** (ASCII → `CacheableASCIIString=87`, non-ASCII →
  `CacheableString=51`, huge variants similar). GetAll keys section
  full wire: `[52][arrayLen][43][87][u16 length][bytes][N × key]`.
- Our `BigEndianBinaryWriter.WriteString` agrees with cppcache; only
  the unit test expectation needed correcting.

Tests: `TcrMessageBuilderPutAllTests` (3: header / per-part wire /
empty map) + `TcrMessageBuilderGetAllTests` (3: header / per-part wire
including `CacheableASCIIString` prefix in class header / empty keys) +
`TcrMessageBuilderRemoveAllTests` (3 — landed late but belongs in
1.3.b); 509 unit total. Integration: 3 PutAll cases + 3 GetAll cases
against a real server.

Deferred:

- `PutAllWithCallback(108)` / `GetAllWithCallback(107)` — builder takes
  the callback parameter but throws `NotSupportedException`; switching
  the msg type one line + adding the `IRegion` overload is all that's
  needed when required.
- Multi-keys spanning chunk boundary (`_keysOffset` advance path) not
  exercised — single-chunk happy path is. Triggering requires shipping
  enough keys for the server framer to split.
- `_exceptions` / `_resultKeys` accumulators declared but not exposed
  publicly (Phase 3+ exception path / Phase 4+ single-hop).

#### Phase 1.3 shared decisions

- Bulk ops take `IReadOnlyDictionary` / `IReadOnlyCollection`; return
  new `Dictionary` / `IReadOnlyDictionary` (.NET convention + don't
  leak internal mutable state).
- versionTag fully discarded (read and dropped), same as Phase 1.2
  `RemoveAsync`. Phase 4 client-side cache / delta fills it back.
- **Key type constraint**: `IRegion<TKey, TValue>` has `where TKey :
  IEquatable<TKey>` (.NET equivalent of cppcache `CacheableKey`'s
  `operator==` + `hashcode()`):
  - Compile-time blocks `byte[]` (`Array` doesn't implement
    `IEquatable<T>`), collections (`List<>` / `Dictionary<>` /
    `HashSet<>`), and POCOs without `IEquatable<T>`.
  - PDX user classes (Phase 2) will need to implement `IEquatable<T>`,
    forcing the user to face Java server-side `equals` / `hashCode`
    semantics.
  - **Types without a converter are only blocked at runtime**:
    `IRegion<MyType, ...>` compiles, but
    `SerializationRegistry.WriteObject` throws `NotSupportedException`
    when `_byType[typeof(MyType)]` misses (existing behaviour, no
    change).
  - Non-generic `IRegion` doesn't add the constraint (untyped
    `GetRegion` returns it; the cast to the generic version blocks at
    compile-time).

---

### Phase 1.2 — Single-key CRUD (int32 KV walking skeleton)

`IRegion<int,int>` 4 ops (Put / Get / Remove / ContainsKey) end-to-end
through a real Apache Geode server. First demo-able milestone.

Region lookup path, serialization (Int32/Boolean converters +
EventIdGenerator), and wire messages (Put(7) / Request(0) / Destroy(9)
/ ContainsKey(38)) all routed through `SerializationRegistry`.
Key/value/callbackArgument all take the same path with no inline type
guards.

**Lesson — cppcache scope parity** (now in memory
`cppcache-scope-parity.md`):

`ClientProxyMembershipIdBuilder.s_uniqueTag` was originally
`static readonly` (process-wide singleton), but cppcache
`ClientProxyMembershipIDFactory::randString_` is an **instance member**
(one per `CacheImpl`). Two `Cache` instances in the same process shared
a clientId; combined with each having its own `EventIdGenerator`
starting at seq=1, the server's `ClientHealthMonitor` treated the
second `(clientId, threadId=1, seq=1)` as a duplicate event and
**silently dropped** it. Put looked successful (no exception) but Get
returned 0 and ContainsKey returned false. Fix: make `_uniqueTag`
instance, generated in ctor. General rule: for bucket-2 cppcache
classes, mirror every field's `instance` / `static` / `thread_local`
scope; don't unilaterally "optimise" to static.

Tests: 161 units + 5
[RegionCrudIntegrationTests](tests/Geode.Client.IntegrationTests/RegionCrudIntegrationTests.cs)
(Put→Get / Get missing / ContainsKey trace / Remove missing / Put
override) all green against a real server, with 3s
`FreshConnectionSettleDelay` to dodge the cold-container race.

Deferred to later phases: built-in DSFID type codecs beyond int32/bool
(Phase 1.3.0 covers most), `callbackArgument` overloads on the public
API (wire is ready but `IRegion` doesn't expose), fresh-conn race
proper fix (Phase 1.5).

---

### Phase 1.1 — Single server connection

`Cache.EnsureInitializedAsync` / `CloseAsync` end-to-end opens a server
connection, runs handshake, sends Ping, and shuts down cleanly. **No**
pool, **no** multi-endpoint, **no** failover.

Foundation (protocol layer): `BigEndianBinaryReader` /
`BigEndianBinaryWriter`, `TcrPart` / `TcrMessage` / `TcrPartBuilder` /
`TcrMessageBuilder`, `ClientProxyMembershipIdBuilder`, `MessageType`
enum, `TcrConnection` skeleton + handshake bytes, `PingIntegrationTests`
green against a real server.

Cache wiring: `TcrEndpoint.CreateNewConnectionAsync` opens socket +
handshake; `Cache.InitializeCoreAsync` takes single host:port from
options; `Cache.CloseAsync` sends `CloseConnection(18)` and releases
the connection. Ping loop via `ThinClientPoolDM.PingLoopAsync` +
`PingServerLocalAsync` runs end-to-end.

Phase-end cleanup: `IValidateOptions<GeodeClientOptions>` enforces
`Pools.Count >= 1` / non-blank `Pool.Name` / `Locators+Servers >= 1` /
valid `CacheHostPortOptions` / `MinConnections >= 0` / `MaxConnections
>= MinConnections`. Three `AddGeodeClient` overloads chain
`.ValidateOnStart()`. Per-scope `CacheScopeContext` fixed the
architectural error where `IOptions<T>.Value` always returned the
default-named instance (named-only registration scenarios). Per-cache
singleton-like services (`Cache` / `TcrConnectionManager` /
`PoolManager` / `ClientProxyMembershipIdBuilder` / `CacheScopeContext`)
are Scoped; per-scope-N-instance types (`ThinClientPoolDM` /
`TcrEndpoint` / `TcrConnection`) keep `ActivatorUtilities`.

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

## Phase 2+ — Custom objects, security, performance, partitioning

See [CLAUDE.md](.claude/CLAUDE.md) Phase 2 / 3 / 4.

### Locator follow-ons (deferred from Phase 1.5)

- **Fixture NAT fix** so locator-mode Put/Get can run:
  `--hostname-for-clients=<host>` + `WithPortBinding(40404, 40404)` to
  pin the server port mapping. Needed for locator-mode integration
  tests in Phase 2+ once subscription / CQ work needs them.
- **`getEndpointForNewCallBackConn`** — subscription channel
  (Phase 2 Continuous Query).
- **`getAllServers`** — Phase 4 single-hop bucket-to-server resolution.
- **`ClientReplacementRequest`** — Phase 4 failover swap (locator
  picks a replacement server when an EP falls out).

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
