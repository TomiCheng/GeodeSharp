# Public API — 行為覆蓋地圖

> 對外公開 API 上的「可驗證行為主張」清單,按 domain 分節。
> 每條 bullet 是一個 test case 或一組行為承諾;狀態符號代表測試狀況。
> 移植原則見 [CLAUDE.md](CLAUDE.md) §移植三原則(預設原則一:鏡像
> cppcache 架構)。
>
> **Living document。** 新冒出來的行為主張就加一條;測試從紅轉綠就
> 把 🔨 改成 ✅。

## 狀態符號

實作生命週期由淺到深(每一階都涵蓋前一階):

| 符號 | 意思 |
| --- | --- |
| ⏳ | 連主張都還沒擺出來(連 public 宣告都沒) |
| 🧩 | API 建立、無實作(public 殼在,還沒 body) |
| 🔨 | API 實作為 NIE stub(body 丟 `NotImplementedException`) |
| 🚧 | API 部分實作,仍有 TODO / dormant NIE(主線通,邊角未填) |
| ✅ | API 已單元測試通過 |
| 🌐 | API 已整合測試通過(Testcontainers 真 server) |
| ❌ | 不做(out of scope) |

> 舊 bullet 多半還停在粗粒度的 ✅/🔨 — living document,逐條 audit
> 時才細分到上面的生命週期。本次新標的條目用細粒度。

---

# Cache Factory

對應 cppcache `CacheFactory` / clicache `CacheFactory`。
C# 公開介面:`Geode.Client.IGeodeCacheFactory`。

機制改了(builder → DI factory);語意保持。

## 註冊
- ✅ `services.AddGeodeClient(name, configure)` 註冊 named cache
- 🔨 多個 named cache 隔離(不同 name 不共享連線 / config)
- 🔨 同名重複註冊行為(覆蓋 vs 拋例外)

## 解析
- 🔨 `Get(name)` 回對應的 `IGeodeCache`
- 🔨 未註冊的 name 拋例外
- 🔨 同名多次 `Get` 回同一 instance(singleton-per-name)

---

# Cache

對應 cppcache `RegionService` / `GeodeCache` / `Cache`(三層抽象)。
C# 公開介面:`Geode.Client.IRegionService` ← `Geode.Client.IGeodeCache`。

## Lifecycle
- ✅ `IsClosed` 反映狀態
- ✅ `CloseAsync` 釋放連線資源
- ✅ `IAsyncDisposable` 支援 `await using`
- 🔨 close 後再操作拋例外
- 🔨 重複 close 安全(idempotent)

## Identity
- 🔨 `Name` 回 DI 註冊時的名稱
- 🔨 `EnsureInitializedAsync` 冪等(多次 call 不重連)
- 🔨 init 失敗時 propagate 原因例外

## Region 入口
- 🔨 `GetRegion(name)` 取非泛型 region
- 🔨 `GetRegion<K,V>(name)` 取 typed region
- 🔨 不存在的 region name 行為(回 null vs 拋)
- ⏳ `RootRegions` 列頂層 region
- ⏳ `CreateRegionFactory` 建 region

## 其他服務(cppcache 有,我們等需求出現再開)
- ⏳ `GetCacheTransactionManager`
- ⏳ `GetPoolManager`(目前只給 internal 用)
- ⏳ `CreateAuthenticatedView`(multi-user 場景)

---

# Pool

對應 cppcache `Pool` / `PoolManager` / `PoolFactory`。
C# 目前 internal-only(沒 MVP consumer 用例);這節列「未來升 public 後該有什麼行為」。

## 生命週期
- 🔨 cache init 時 pool 自動建好
- 🔨 cache close 時 pool 連線釋放
- 🔨 endpoint 全死拋 `NoAvailableLocatorsException`

## 連線數
- 🔨 連線數不超過 `MaxConnections`(超出拋 `AllConnectionsInUseException`)
- 🔨 閒置時不低於 `MinConnections`
- ⏳ idle timeout 觸發回收

## 監控(升 public 才需要)
- ⏳ 連線數 / endpoint 狀態查詢
- ⏳ 連線事件通知

---

# Region

對應 cppcache `Region` / clicache `IRegion<TKey,TValue>`。
C# 公開介面:`Geode.Client.IRegion` + `Geode.Client.IRegion<TKey,TValue>`(typed sugar)。

## CRUD(server-side)
- 🌐 `PutAsync` pool-mode 走 wire(`ThinClientRegion.PutNoThrowRemoteAsync` 翻 cppcache `putNoThrow_remote`:delta gate + `PUT_DELTA_ERROR` retry + reply switch)。Local 走 base no-op + local map。整合測試:pool-mode `AfterPut` 真寫 server;單元:`ServerOptional` 包
- 🔨 `Put` null key 拋例外
- 🔨 `Put` 在 closed cache 拋例外
- ✅ `GetAsync` miss 回 null(Local/LocalEntryLru never-put → null;pool-mode `ServerOptional` 包)
- ✅ `GetAsync` 命中回值(Local/LocalEntryLru `Put`→`Get` 走 local entry map;`GetNoThrowAsync` 翻 cppcache `getNoThrow` 全鏈)
- 🚧 `Get` 經 caching-proxy 命中 local 不發 wire(local-hit 短路:單元 `ServerOptional` 軟過,尚未硬驗)
- 🔨 `Remove` 不存在的 key 不拋
- 🔨 `Remove`(strict)值不符回 false
- ✅ `DestroyAsync` 移除已存在 entry(LocalCount 1→0)
- ✅ `DestroyAsync` 對 missing key lenient 靜默成功(對映 cppcache `destroy()` NORMAL flag,afterRemote 容忍 not-found)
- 🌐 `InvalidateAsync` 清值留 key:put → invalidate → 值清(get miss 觸發重載)、key 留(對比 `DestroyAsync` 移除整 entry)。管線 `InvalidateActions`(鏡像 Put/Destroy)→ `InvalidateLocalAsync`(cppcache `LocalRegion::invalidateLocal`)→ `ConcurrentEntriesMap.InvalidateAsync`(攤平 `ConcurrentEntriesMap::invalidate` + `MapSegment::invalidate`:設 `CacheableToken.Invalid`、留 key、tombstone/absent → `CacheEntryNotFound`);remote 走 `ThinClientRegion.InvalidateNoThrowRemoteAsync`(`TcrMessageInvalidate`)。單元:Local/LocalEntryLru;整合:CachingProxy `put→invalidate→get` round-trip 回 null
- 🔨 `Clear` 清空 region
- ✅ `ContainsKeyAsync` 本地查(5 種 RegionShortcut;Proxy 永遠 false,caching 走 local map,tombstone 算 false)
- 🌐 `ContainsKeyOnServerAsync` 查 server:LocalRegion 拋 `NotSupportedException`,ThinClientRegion 走 wire。10 單元(5 shortcut × true/false,wire-going 用 `ServerOptional`)+ 6 整合(真 server:`AfterPut`→true / `NeverPut`→false)
- 🔨 `ExistsValue` 跑 OQL predicate
- 🔨 `SelectValue` 跑 OQL predicate

## Bulk
- 🔨 `PutAll` 一次寫多筆
- 🔨 `GetAll` 一次讀多筆
- 🔨 `RemoveAll` 一次刪多筆

## 嚴格語意(throw on miss/exists)
- 🔨 `Create` 重複拋 `EntryExistsException`
- 🔨 `LocalDestroyAsync` 缺漏拋 `EntryNotFoundException`(對映 cppcache `localDestroy()` LOCAL flag 的 strict 語意 — API 未實作)
- ✅ `DestroyAsync` 對 missing 不拋(lenient,跟 cppcache `destroy()` NORMAL flag 一致;見 CRUD 段) 

## Local(本地快取操作)
- 🔨 `LocalPut` 只寫本地不發 wire
- 🔨 `LocalCreate` 只建本地
- 🔨 `LocalInvalidate` 標 local entry 為 invalid
- 🔨 `LocalDestroy` 刪 local entry
- 🔨 `LocalRemove` / `LocalRemoveEx`
- 🔨 `LocalClear` 清空 local map
- 🔨 `LocalInvalidateRegion` 整 region local 標 invalid

## LRU eviction
對應 cppcache `LRUEntriesMap` / `LRUAction` / `EvictionController`。

### LRU Type

- ✅ Count LRU
- 🚧 Heap LRU(端到端可動;偏離見 NOTE.md)

### LRUAction(4 subclass)
> `EntriesMapFactory` 只依 DiskPolicy 在 `LocalDestroy` / `OverflowToDisk`
> 間選(line 63 寫死,不讀 `LruEvictionAction`,鏡像 cppcache)→
> `Destroy` / `Invalidate` 結構性不可達。

- ✅ `LRULocalDestroyAction`(`LocalDestroy`,count + heap 預設)
- 🚧 `LRUDestroyAction`(`Destroy`)— 實作完成,但 factory 不選此 action(不可達 + 無測試)
- 🔨 `LRULocalInvalidateAction`(`Invalidate`)— 不可達(factory 不選此 action);
  region `InvalidateAsync` 已實作,此 action 的 `EvictAsync` 仍待接
- 🔨 `LRUOverFlowToDiskAction`(`OverflowToDisk`)— value 寫磁碟
  (`IPersistenceManager.WriteAsync`)+ 記憶體換 overflow token;待 Phase 4
  persistence manager 實作

## 列舉 / 巡訪
- 🔨 `Keys` / `Values` / `Entries` 列 local
- 🔨 `ServerKeys` 列 server
- 🔨 `Size` 回 entry 數
- 🔨 `GetEntry` 回單筆快照
- 🔨 `IsDestroyed` 反映狀態

## Sub-region
- 🔨 建 sub-region
- 🔨 取 sub-region
- 🔨 列出所有 sub-region
- 🔨 銷毀 sub-region

## Region lifecycle
- 🔨 `DestroyRegion` 通知 server
- 🔨 `InvalidateRegion` 通知 server

## Attributes
- 🔨 `Attributes` 回快照
- 🔨 `GetAttributesMutator` 可改動態欄位
- ✅ `IRegionFactory.SetCacheLoader` / `SetCacheWriter` / `SetCacheListener` fluent 入口(塞進 `RegionAttributes`;loader 已接 get 路徑,writer/listener 見下)

## Cache callbacks(loader / listener / writer)

> NOTE.md 原列為 out-of-scope;本次重新納入(純 managed user hook,無
> wire / subscription 相依即可運作)。`ICacheLoader` / `ICacheListener` /
> `ICacheWriter` interface + `IDataInput` / `IDataOutput` 占位介面已 public。

- ✅ `ICacheLoader` read-through:get **全 miss**(local + remote 都空)時呼叫 `LoadAsync` 回填(`LocalCacheLoaderTests`:`Get(5, callback:3)` → `5+3=8`)。對映 cppcache `getNoThrow` loader fallback
- ✅ `ICacheListener` after-event dispatch:`InvokeCacheListenerForEntryEvent` 依 `EntryEventType` 派 `AfterCreate`/`AfterUpdate`/`AfterDestroy`/`AfterInvalidate`,`Listener` 從 `Attributes.CacheListener` 接線(ctor)。AFTER_UPDATE 判別式忠實(`oldValue \|\| isNotificationUpdate \|\| isLocal`)。`LocalCacheListenerTests`:新 key→afterCreate、既有 key→afterUpdate、無 listener no-op
- ✅ `ICacheListener` stat:`CacheListenerCallCompleted`(cache)+ `ListenerCall`(region time)正確記錄(meter capture 驗)
- ✅ listener callback 拋例外 → `CacheListenerException`(對映 `GF_CACHE_LISTENER_EXCEPTION`;`OperationCanceledException` 例外放行)
- ✅ `ICacheWriter` veto(`beforeCreate` / `beforeUpdate`):`InvokeCacheWriterForEntryEvent` 依 `EntryEventType` 派 `BeforeCreate`/`BeforeUpdate`Async,回 `false` 或拋例外 → op 中止拋 `CacheWriterException`。`Writer` 從 `Attributes.CacheWriter` 接線(ctor)。對映 cppcache `invokeCacheWriterForEntryEvent`(LocalRegion.cpp:2573-2641)。`LocalCacheWriterTests`:allow→成功、veto→不寫、throw→veto、existing→beforeUpdate、無 writer no-op
- ✅ `ICacheWriter` stat:`WriterCall`(region time)記錄(`Put_RecordsWriterStats` 驗 fire 一次;cppcache writer 路徑無 cache-level counter)
- 🚧 `ICacheWriter` `beforeDestroy`:dispatch case 在 + 經 `DestroyActions.BeforeEventType` 可達,但無 destroy-veto 測試
- 🧩 `ICacheWriter` region-event veto(`beforeRegionClear` / `beforeRegionDestroy`):介面宣告了,但 cppcache `invokeCacheWriterForRegionEvent`(LocalRegion.cpp:2643-2691)未 port
- 🧩 `ICacheWriter` / `ICacheListener` / `ICacheLoader` `Close`:介面宣告了,writer/listener-detach 與 region-close 時的呼叫未接
- 🔨 `UpdateAccessAndModifiedTimeForEntry` entry-level expiry touch:外層 guard 翻好,但 `EntryExpiryEnabled=true` 時 body NIE(`ExpEntryProperties` surface 已建,寫入未接)

## Interest list / subscription
- ⏳ 註冊單 key 接 server 推播
- ⏳ 註冊全 key
- ⏳ 註冊 regex
- ⏳ 反註冊
- ⏳ 列出已註冊的 key / regex
- ⏳ durable + initial-values + receive-values 三組 flag 組合

## Back-ref
- 🔨 `RegionService` 回所屬 cache

---

# Serialization

對應 cppcache `Serializable` / `DataSerializable` / `PdxSerializable`。
C# 公開介面:`IDataConverter` / `IPdxSerializable<TSelf>` / `IPdxSerializer<T>`。

## Heap sizing(`GetObjectSize`,對映 cppcache `Serializable::objectSize`)
- 🚧 非 PDX(`IDataConverter`)
- 🔨 PDX(`IPdxSerializable` / `IPdxSerializer`)

---

# Query

對應 cppcache `QueryService` / `Query<T>`。
C# 公開介面:`Geode.Client.IQueryService` + `Geode.Client.IQuery<T>`。

## QueryService
- 🔨 `NewQuery<T>(oql)` 建 query
- 🔨 invalid OQL 在 execute 時拋 `QueryException`

## Query 執行
- 🔨 `ExecuteAsync` 跑簡單 select
- 🔨 結果可枚舉
- 🔨 空結果集
- 🔨 timeout 經 `CancellationToken` 取消
- ⏳ projection / typed row
- ⏳ parameterised query

## CQ(continuous query,Phase 4+)
- ⏳ 註冊 CQ
- ⏳ CQ 事件回呼
- ⏳ CQ 生命週期(close / stop)

---

# Exceptions

cppcache `ExceptionTypes.hpp` 58 個 exception。原則二能 BCL 取代的就直接用 BCL,不另開子類;剩下 Geode-runtime 語意才開 `GeodeException` 子類。

## 已實作子類(拋出條件)
- ✅ `GeodeException` — 所有 Geode 子類的 base
- ✅ `NoAvailableLocatorsException` — pool retry loop 全失敗
- ✅ `CacheServerException` — server 回 `MessageType.Exception`
- ✅ `AuthenticationFailedException` — auth 拒絕
- ✅ `AuthenticationRequiredException` — server 要求 auth,client 沒帶
- ✅ `NotAuthorizedException` — auth pass 但無權限
- ✅ `NotConnectedException` — wire 斷
- ✅ `AllConnectionsInUseException` — pool 滿
- 🔨 `EntryNotFoundException` — class 已建,尚無 consumer 拋(等 `LocalDestroyAsync` strict path 上線觸發)
- 🔨 `EntryExistsException` — class 已建,尚無 consumer 拋(等 `Create` strict path 上線觸發)
- 🔨 `RegionDestroyedException` — region 已銷毀(目前走 text-coded `CacheServerException`)
- 🔨 `CacheLoaderException` — class 已建 + consumer 已接(`GetNoThrowAsync` loader catch 包 `LoadAsync` 例外);throw path 未測
- 🔨 `CacheListenerException` — class 已建 + consumer 已接(`InvokeCacheListenerForEntryEvent` catch 包 listener callback 例外,`OperationCanceledException` 放行);throw path 未測
- ✅ `CacheWriterException` — writer veto(回 `false` 或 callback 拋例外)時拋(`LocalCacheWriterTests` 驗)

## BCL 取代(不開子類)
| cppcache | BCL |
| --- | --- |
| `IllegalArgumentException` | `ArgumentException` 系列 |
| `IllegalStateException` | `InvalidOperationException` |
| `TimeoutException` | `System.TimeoutException` |
| `InterruptedException` | `OperationCanceledException` |
| `UnsupportedOperationException` | `NotSupportedException` |
| `NullPointerException` | `NullReferenceException` / `ArgumentNullException` |
| `OutOfRangeException` | `ArgumentOutOfRangeException` / `IndexOutOfRangeException` |
| `BufferSizeExceededException` | `System.IO.InvalidDataException` |
| `MessageException` | `System.IO.InvalidDataException` |
| `GeodeIOException` | `System.IO.IOException` |
| `ClassCastException` | `InvalidCastException` |
| `ConcurrentModificationException` | `InvalidOperationException` |
| `FileNotFoundException` | `System.IO.FileNotFoundException` |
| `NotOwnerException` | `SynchronizationLockException` |
| `OutOfMemoryException` | `System.OutOfMemoryException` |
| `AssertionException` | `Debug.Assert` |
| `UnknownException` | generic `Exception` |
| `CacheXmlException` | N/A — 不解析 `cache.xml` |

## 待開(測試踩到才加)
- ⏳ `CacheClosedException`
- ⏳ `RegionExistsException`
- ⏳ `CqException` 系列
- ⏳ `FunctionException`
- ⏳ `TransactionException` / `RollbackException`
- ⏳ `CacheWriterException` / `CacheLoaderException` / `CacheListenerException`
- ⏳ 其他(`AlreadyConnectedException` / `InvalidDeltaException` / `DuplicateDurableClientException` …)

