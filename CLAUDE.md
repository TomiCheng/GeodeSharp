# GeodeSharp — Project Context

> This file is Claude Code's long-term project memory. Read it once at
> the start of every session, confirm the current phase, then start
> work.
>
> **This is a living document.** Update it at the end of each phase
> with what was learned; phase boundaries are deliberately fuzzy and
> may be adjusted as needed.

---

## One-line goal

Build a **pure-managed, zero-runtime-dependency, cross-platform** Apache
Geode client targeting **.NET 10 (LTS)** and ship it on NuGet.

Upstream reference: <https://github.com/apache/geode-native>
(We do **not** port the C++/CLI `clicache/` — too restricted and
Windows-only.)

Project naming (two layers, deliberately separate):
- GitHub repo / local folder: `GeodeSharp` (https://github.com/TomiCheng/GeodeSharp)
- Solution: `geode-dotnet.sln`
- NuGet PackageId / Root namespace / AssemblyName: `Geode.Client`
- Source folder: `src/Geode.Client/`; tests `tests/Geode.Client.Tests/`
  and `tests/Geode.Client.IntegrationTests/`; sample
  `samples/Geode.Client.Sample/`

"GeodeSharp" is the project / repo name (the human-facing identifier);
the assembly layer uses `Geode.Client` (consistent with industry
convention: the .NET client for Apache Geode). Code, `using`
directives, and `<PackageReference>` entries always use `Geode.Client`;
GeodeSharp is reserved for talking about the project itself.

---

## Architectural decisions (settled — do not relitigate)

### Why not the alternatives

- **Route A (port C++/CLI to .NET 10):** rejected. Microsoft has stated
  C++/CLI on .NET Core is supported for compatibility only, with no
  future investment, Windows-only, no AOT, no SDK-style projects.
- **Route B1 (keep native cppcache, add a P/Invoke wrapper):** rejected.
  Forces us to maintain native binaries per RID, loses the "pure
  managed" benefit, and the C ABI shim is a project of its own.
- **Route B2 (pure managed, speak the wire protocol ourselves):**
  ✅ **adopted.**

### B2's trade-offs and how we cope

The Geode wire protocol has **no normative spec** (Apache's own wiki
admits this). It has to be reverse-engineered from `cppcache/src/` and
Java `geode-core`.

**Mitigation 1:** treat cppcache as the "executable spec" — read it
rather than designing the protocol from scratch.
**Mitigation 2:** scope features in phases. Ship the MVP first, then
fill out the rest incrementally.

### Port + modernise

cppcache `clicache/` has already validated all the interface shapes,
naming, and semantics. Our work is "translate + modernise", not
"design from scratch":

- **Keep:** type names (`IRegion`, `IGeodeCache`, `IQueryService`),
  method names (Put / Get / Remove), core concepts (Region, Pool,
  QueryService).
- **Modernise:** sync → async, `gcnew` → record/class, cache.xml →
  `IOptions<T>`, static factory → DI.
- **Public surface uses C# `interface`, never `abstract class`.**
  cppcache types in `cppcache/include/geode/` (e.g. `Cache`, `Region`,
  `RegionService`) that we choose to expose go out as **C#
  `interface`** (`IGeodeCache`, `IRegion<TKey,TValue>`,
  `IRegionService`); concrete types live `internal sealed`.
  Visibility map: `cppcache/include/geode/Foo.hpp` → C# `IFoo`
  (visibility decided per-class, not auto-public — cppcache puts
  things in `include/` because C++ has no `internal`; .NET does, so
  default to internal unless a real consumer use case demands
  public, then upgrade);
  `cppcache/src/FooImpl.hpp` (Pimpl body) → internal `Foo` (Pimpl
  collapsed); `cppcache/src/Bar.hpp` (no public abstract) →
  internal.

### Three-bucket porting rule

For every cppcache class we encounter, decide which bucket it falls
into and act accordingly. When in doubt, default to **bucket 2**
(mirror) — same logic as the "mirror then prune" config policy.

The actual class-by-class mapping (cppcache name → C# name, bucket,
visibility, status, phase) lives in [PORTING.md](PORTING.md). Add a
row whenever you encounter a new cppcache class.

#### Bucket 1: BCL fully covers it → **don't implement**

cppcache built these because C++ standard / boost gave them the
primitives but not the abstraction. .NET has the abstraction
out-of-the-box. Use the BCL type directly; do not port the cppcache
class.

The concrete cppcache ↔ BCL mapping table lives in
[PORTING.md](PORTING.md) under "Bucket 1 — BCL replacements". Add
new mappings there as you encounter them.

#### Bucket 2: domain logic / wire protocol → **mirror the architecture**

These are what we are actually writing. Match cppcache class names,
file layout, inheritance, and method names; modernise only the
mechanics (sync → async, multi-inheritance → composition, etc.).

Examples: `ThinClientBaseDM`, `DistributionManager`, `PoolDM`,
`TcrEndpoint`, `TcrPoolEndPoint`, `TcrConnection`,
`TcrConnectionManager`, `ThinClientLocatorHelper`, `TcrMessage`,
`Cache`, `CacheImpl`, `Region`, `ThinClientRegion`,
`ClientMetadataService` (Phase 4), `ThinClientStickyManager`
(Phase 6), `PdxType` / `PdxTypeRegistry` (Phase 2).

#### Bucket 3: BCL partially covers, semantics incomplete → **thin wrapper**

Use the BCL type as the engine; wrap **only enough** to add the
missing semantics. Do not rebuild the whole cppcache class.

The concrete cppcache ↔ wrap-strategy table lives in
[PORTING.md](PORTING.md) under "Bucket 3 — thin wrappers". Add new
entries there as you encounter them.

#### Rule 4: when ambiguous → default to bucket 2

If a cppcache class doesn't clearly fit bucket 1 or 3, mirror it
(bucket 2) as a stub first. During wiring we'll discover whether it
collapses to BCL (move to bucket 1) or shrinks to a wrapper
(bucket 3). Same "mirror then prune" discipline as Options.

---

## Overall principles

1. **Async-first.** All I/O operations expose only an async API; no
   synchronous variants.
2. **Options pattern.** Configuration flows through `IOptions<T>`
   bound to `appsettings.json`.
3. **DI-first.** Registration via `services.AddGeodeClient(...)`; no
   static singletons.
4. **Zero external runtime dependencies.** Everything sits on the BCL;
   the only references are the `Microsoft.Extensions.*` abstraction
   packages.
5. **API-first / interface-first.** Declare interface shells first
   (`NotImplementedException` bodies), then fill in implementations;
   interfaces are translated from cppcache `clicache/`.
6. **Walking skeleton.** Each sub-phase delivers an end-to-end minimum;
   never finish a whole layer before any layer above it works.
7. **Living document.** This file evolves alongside development.

---

## Dependency policy

| What cppcache uses    | Our replacement                                                 |
| --------------------- | --------------------------------------------------------------- |
| Boost.Asio            | `System.Net.Sockets` + `System.IO.Pipelines` + `Channels`       |
| OpenSSL               | `System.Net.Security.SslStream`                                 |
| Xerces-C (cache.xml)  | **Cut entirely.** Use `Microsoft.Extensions.Configuration`.     |
| SQLite (overflow)     | Not implemented.                                                |
| Google Test / Benchmark | xUnit v3 / BenchmarkDotNet                                    |

---

## Configuration

cppcache uses two files: a `.ini` (`SystemProperties`) and `cache.xml`
(region / pool declarations parsed by Xerces). **We replace both with
the .NET `IOptions<T>` pattern** — `appsettings.json` + `IConfiguration`
binds straight to record / class options. **No `cache.xml`. No `.ini`.**

### Options policy

1. **Mirror, then prune.** When porting cppcache config, **copy every
   property first** (one C# property per cppcache key, defaults
   matching cppcache constants). Pruning happens once, late — likely
   end of Phase 1.5 or before the first NuGet release — when we audit
   which properties any code path actually reads. Do not pre-judge
   "this looks unused" while porting; the cppcache audit window stays
   open until the .NET pool design is settled.

2. **Document semantics on the property, not in side notes.** Every
   options property's XML doc must capture what was learned by reading
   cppcache: which file consumes it, what the value actually drives
   (e.g. `SO_SNDBUF`, expiry-task interval, per-endpoint cap), whether
   it's pool-level / connection-level / endpoint-level, and any
   platform-specific quirks (`#ifdef __linux` etc.). The doc is the
   audit trail — anyone reviewing the property six months later
   should not need to re-read cppcache to understand it.

3. **No invented schema ahead of implementation.** Concrete JSON
   shapes are decided phase-by-phase against cppcache
   `SystemProperties` semantics; do not write a target schema in this
   doc that the code hasn't reached yet.

---

## Public API sketch (DI-first)

```csharp
// Registration
builder.Services.AddGeodeClient(builder.Configuration.GetSection("Geode"));

// Usage
public class OrderService(IGeodeCache cache)
{
    private readonly IRegion<string, byte[]> _orders = cache.GetRegion<string, byte[]>("orders");
    public Task SaveAsync(string id, byte[] payload, CancellationToken ct)
        => _orders.PutAsync(id, payload, ct);
}
```

Current interface shape lives in `src/Geode.Client/` — `IGeodeCache`,
`IRegion` / `IRegion<TKey,TValue>` (typed overlay with
`where TKey : IEquatable<TKey>`), `IQueryService`, `IQuery<T>`. Use
the source as the canonical reference; this file no longer carries a
parallel interface listing.

**Important:** in MVP we do not support cache.xml or region creation.
A DBA pre-creates the region with gfsh
(`gfsh create region --name=test --type=REPLICATE`); the client only
acts as a proxy.

---

## Feature phases

### Phase 1 (MVP — a production-ready client)

- Connect
- Single-key CRUD (Put / Get / Remove / ContainsKey)
- Bulk operations (PutAll / GetAll / RemoveAll)
- Clear
- Invalidate
- Region convenience queries (ExistsValue / SelectValue)
- Built-in type serialisation (including collections: List, Dictionary,
  arrays, HashSet)
- OQL queries (`SELECT *`, `SELECT COUNT(*)`, and multi-column
  projection `SELECT field1, field2` — pulled forward from Phase 2
  because the `StructSet` branch in the result decoder is on the same
  code path as `ResultSet`; deferring it would leave a half-built
  switch with a silent-corruption failure mode for projection OQL)
- Connection pool
- Locator discovery
- Server failover / automatic reconnect

### Phase 2 (custom objects + advanced query)

- Custom-object serialisation (PDX)
- Interop with the Java client
- PdxInstance (read fields without full deserialisation)
- Continuous Query (server-push subscriptions)
- Transactions (Begin / Commit / Rollback)

### Phase 3 (security + compute)

- Authentication (username/password, custom auth providers)
- TLS / mTLS
- Function execution (server-side)

### Phase 4 (performance + sharding)

- Delta propagation (ship only changed fields)
- Partition resolver (custom colocation)

### Not implemented

- **cache.xml** — replaced by `appsettings.json` + `IOptions<T>`.
- **Sub-regions** — Geode itself discourages them.
- **Synchronous APIs** — async only.
- **Cache listener / loader / writer** — niche use cases; easier to
  implement server-side in Java.
- **Region expiration / eviction** — managed by server-side
  configuration; the client stays out.

---

## Phase 1 sub-phase breakdown

Phase 1 is split into 5 dependency-ordered sub-phases (1.1 single
connection → 1.2 single-key CRUD → 1.3 bulk + management → 1.4 OQL
query → 1.5 connection management), each a walking skeleton. The
per-sub-phase scope, status, design decisions, and "踩過的坑" notes
live in [PROGRESS.md](PROGRESS.md).

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

### Handshake (the easiest place to get burned)

The handshake does **not** use the standard frame format — it's an
ad-hoc byte sequence. Translate it byte-by-byte against
`cppcache/src/TcrConnection.cpp::sendHandshakeForServer`. **Do not
work from memory.**

### MessageType

The canonical list is the `Geode.Client.Protocol.MessageType` enum
in `src/Geode.Client/Protocol/MessageType.cs` (mirrored from
`cppcache/src/TcrMessage.hpp`). Which values land in which sub-phase
is tracked in [PROGRESS.md](PROGRESS.md).

---

## Implementation principles (keep these in mind)

1. **Read cppcache before designing protocol.** `TcrMessage.cpp`,
   `TcrConnection.cpp`, `HandShake.cpp`, and `ThinClientPoolDM.cpp` are
   the spec.
2. **API-first.** Declare interface shells first
   (`NotImplementedException`), then fill in.
3. **Walking skeleton.** Each phase runs end-to-end before stacking
   the next layer.
4. **Frame codec must have unit tests** backed by Wireshark byte
   fixtures.
5. **Don't over-abstract.** Write concrete classes at the lower
   layers; only extract interfaces when DI wiring lands in Phase 1.2.
6. **Big-endian everywhere** (`BinaryPrimitives.WriteInt32BigEndian`).
   Geode is Java; the wire is network byte order.
7. **A single connection already supports concurrency** (pipelined
   requests keyed by transaction id). The pool is a
   throughput / fault-isolation optimisation, not a baseline
   requirement.
8. **Mirror every cppcache log call.** When porting a bucket-2 class,
   every `LOGFINE` / `LOGINFO` / `LOGWARN` / `LOGERROR` / `LOGDEBUG`
   /`LOGFINER` in the source becomes a `_logger.Log*` call at the
   same point with the same severity (`LogTrace` ≈ `LOGFINER`,
   `LogDebug` ≈ `LOGFINE`/`LOGDEBUG`, `LogInformation` ≈ `LOGINFO`,
   `LogWarning` ≈ `LOGWARN`, `LogError` ≈ `LOGERROR`). Logs are part
   of the observable behaviour we're porting — diagnosing a wire-
   protocol bug against cppcache traces requires the same breadcrumbs
   in the same order. Use `ILogger<T>` injected through DI; format
   args with structured logging (`"Connecting to {Endpoint}"`,
   `endpointName`), not `string.Format`. Where the cppcache message
   text is awkward in English, paraphrase but keep the severity and
   the key data fields.
9. **Constant naming follows source.** Wire-protocol constants that
   mirror a cppcache `static const` keep cppcache's
   `SCREAMING_SNAKE_CASE` verbatim (`FLAG_NULL_TAG`,
   `HAS_MEMBER_ID`, `LAST_CHUNK_MASK`); diagnostics and grep
   round-trip cleanly between sources. Constants we invent on the
   C# side (`MetaTransactionId`, `ThreadId`) use standard C#
   `PascalCase`. `.editorconfig` doesn't enforce — the two
   conventions coexist by intent, distinguished by whether the
   constant has a 1:1 cppcache origin.

---

## Toolchain

- **.NET 10 SDK** (LTS, GA 2025-11)
- **xUnit v3** + FluentAssertions
- **Testcontainers** — integration tests boot `apachegeode/geode`
- **GitHub Actions** — CI on PR / push, release on tag
- **NuGet** — `MinVer` derives the version from git tags
- **Source Link** + `.snupkg`
- **Apache-2.0** licence (matches the upstream project)

---

## Dual-network sync (Tomi's setup)

The maintainer works across two networks:

- **Internet side** — primary development, GitHub, CI, NuGet publish.
- **Intranet side** (air-gapped) — internal CI/CD, internal GitLab /
  GitHub.
- Sync method — USB bare repo.
- Branches — `main` (features), `ci/offline` (CI/CD config,
  **intranet-only**).
- Rule — only reviewed / approved `main` crosses the USB boundary.

**No direct commits to `main`.** All changes go through PR + review.

---

## Bootstrapping the next task

New session: read this file, then [PROGRESS.md](PROGRESS.md), find
the **下一步入口** marker on the most recently completed sub-phase,
and start from there. PROGRESS.md's sub-phase sections carry the
specific context (entry file, prerequisite work, design decisions
already taken) for each upcoming task — there is no per-phase prompt
template to maintain here.
