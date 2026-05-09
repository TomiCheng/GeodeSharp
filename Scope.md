# Scope — cppcache public-header coverage

> Audit of `cppcache/include/geode/*.hpp` (86 headers) against the .NET
> client's MVP. Lists the public surface we plan to mirror, the parts
> we explicitly defer, and where each one lives upstream so the next
> session knows what's been triaged.

Source root: `D:\github\geode-native\cppcache\include\geode\`.

---

## In scope (MVP)

The walking skeleton needs at most these. Others come later or never.

| Role                    | cppcache headers                                                                                              | .NET surface                              |
| ----------------------- | ------------------------------------------------------------------------------------------------------------- | ----------------------------------------- |
| Cache root / lifecycle  | `Cache.hpp`, `GeodeCache.hpp`, `RegionService.hpp`, `CacheFactory.hpp`                                        | `IGeodeCache`                             |
| Region (CRUD)           | `Region.hpp`                                                                                                  | `IRegion<TKey, TValue>`                   |
| Query (OQL)             | `QueryService.hpp`, `Query.hpp`, `ResultSet.hpp`, `SelectResults.hpp`, `Struct.hpp`                           | `IQueryService`, `IQuery<T>`              |
| Pool / connection       | `Pool.hpp`, `PoolFactory.hpp`, `PoolManager.hpp`                                                              | Internal (Phase 6 scope when pool lands)  |
| Serialisation primitives| `Serializable.hpp`, `DataSerializable.hpp`, `DataInput.hpp`, `DataOutput.hpp`, `CacheableKey.hpp`, `CacheableBuiltins.hpp`, `CacheableString.hpp`, `CacheableDate.hpp` | Wire codec (`TcrPart`, `BigEndian*`)      |

## Out of scope (deferred)

Each row maps to one or more cppcache headers we are **not** modelling
in MVP. Don't introduce types that mirror these unless the audit window
explicitly admits them.

| Group                       | Count | cppcache headers                                                                                                                                                                                            | Defer reason                                       |
| --------------------------- | ----- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------- |
| PDX                         | 10    | `Pdx*.hpp` (`PdxInstance`, `PdxInstanceFactory`, `PdxReader`, `PdxWriter`, `PdxSerializable`, `PdxSerializer`, `PdxFieldTypes`, `PdxUnreadFields`, `WritablePdxInstance`, `PdxWrapper`)                      | Lands once PDX work starts                         |
| CQ (continuous queries)     | 13    | `Cq*.hpp` (`CqAttributes`, `CqAttributesFactory`, `CqAttributesMutator`, `CqEvent`, `CqListener`, `CqOperation`, `CqQuery`, `CqResults`, `CqServiceStatistics`, `CqState`, `CqStatistics`, `CqStatusListener`) | Beyond MVP                                         |
| Function execution          | 3     | `Execution.hpp`, `FunctionService.hpp`, `UserFunctionExecutionException.hpp`                                                                                                                                | Beyond MVP                                         |
| Transactions                | 2     | `CacheTransactionManager.hpp`, `TransactionId.hpp`                                                                                                                                                          | Beyond MVP                                         |
| Region attrs / callbacks    | 11    | `RegionAttributes.hpp`, `RegionAttributesFactory.hpp`, `RegionShortcut.hpp`, `RegionEntry.hpp`, `RegionEvent.hpp`, `EntryEvent.hpp`, `AttributesMutator.hpp`, `ExpirationAction.hpp`, `ExpirationAttributes.hpp`, `DiskPolicyType.hpp`, `CacheListener.hpp`, `CacheLoader.hpp`, `CacheWriter.hpp` | Region lifecycle / listener APIs out of MVP        |
| Partition / persistence     | 4     | `PartitionResolver.hpp`, `FixedPartitionResolver.hpp`, `StringPrefixPartitionResolver.hpp`, `PersistenceManager.hpp`                                                                                        | Server-side / overflow concepts                    |
| Auth                        | 2     | `AuthInitialize.hpp`, `AuthenticatedView.hpp`                                                                                                                                                               | Until auth phase lands                             |
| Stats / misc                | 5     | `CacheStatistics.hpp`, `Delta.hpp`, `Properties.hpp`, `SystemProperties.hpp`, `Exception.hpp`/`ExceptionTypes.hpp`                                                                                          | Stats replaced by EventCounters; Properties replaced by `IOptions`; exceptions handled by `GeodeException` + BCL |
| Cacheable extras            | 4     | `CacheableEnum.hpp`, `CacheableObjectArray.hpp`, `CacheableFileName.hpp`, `CacheableUndefined.hpp`, `Serializer.hpp`, `TypeRegistry.hpp`                                                                    | Add only when a consumer needs them                |

## Subdirectories

- `internal/` — not part of the user-facing API. Contains PDX
  internals, framework helpers, serialisation constants. Read for
  reference, do not mirror.
- `util/` — small helpers (`LogLevel.hpp` etc.). Mirrored ad hoc when a
  field needs them.

---

## Header count check

```
total in cppcache/include/geode/*.hpp : 86
in scope                              : 21 (5 cache + 1 region + 5 query/result + 3 pool + 7 serialisation primitives + 0)
out of scope                          : ~54
internal / util subdirs               : 2
```

(Out-of-scope count is approximate — some headers transitively belong
to multiple groups.)
