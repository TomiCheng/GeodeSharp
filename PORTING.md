# Public API — 行為覆蓋地圖

> 對外公開 API 上的「可驗證行為主張」清單,按 domain 分節。
> 每條 bullet 是一個 test case 或一組行為承諾;狀態符號代表測試狀況。
> 移植原則見 [CLAUDE.md](CLAUDE.md) §移植三原則(預設原則一:鏡像
> cppcache 架構)。
>
> **Living document。** 新冒出來的行為主張就加一條;測試從紅轉綠就
> 把 🔨 改成 ✅。

## 狀態符號

| 符號 | 意思 |
| --- | --- |
| ✅ | 有 test 跑過、過 |
| 🔨 | 主張寫了,test 還沒過(或還沒寫) |
| ⏳ | 連主張都還沒擺出來 |
| ❌ | 不做(out of scope) |

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
- 🔨 `Put` 在 5 種 RegionShortcut 都通過
- 🔨 `Put` null key 拋例外
- 🔨 `Put` 在 closed cache 拋例外
- 🔨 `Get` miss 回 null
- 🔨 `Get` 經 caching-proxy 命中 local 不發 wire
- 🔨 `Remove` 不存在的 key 不拋
- 🔨 `Remove`(strict)值不符回 false
- 🔨 `Invalidate` 命中 server
- 🔨 `Clear` 清空 region
- 🔨 `ContainsKey` 查 server
- 🔨 `ExistsValue` 跑 OQL predicate
- 🔨 `SelectValue` 跑 OQL predicate

## Bulk
- 🔨 `PutAll` 一次寫多筆
- 🔨 `GetAll` 一次讀多筆
- 🔨 `RemoveAll` 一次刪多筆

## 嚴格語意(throw on miss/exists)
- 🔨 `Create` 重複拋 `EntryExistsException`
- 🔨 `Destroy` 缺漏拋 `EntryNotFoundException`

## Local(本地快取操作)
- 🔨 `LocalPut` 只寫本地不發 wire
- 🔨 `LocalCreate` 只建本地
- 🔨 `LocalInvalidate` 標 local entry 為 invalid
- 🔨 `LocalDestroy` 刪 local entry
- 🔨 `LocalRemove` / `LocalRemoveEx`
- 🔨 `LocalClear` 清空 local map
- 🔨 `LocalInvalidateRegion` 整 region local 標 invalid

## LocalLRU
- ⏳ 配 LRU eviction 後 `LocalPut` 觸發逐出
- ⏳ LRU 不影響 server-side 操作

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
- 🔨 `RegionDestroyedException` — region 已銷毀(目前走 text-coded `CacheServerException`)

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
- ⏳ `EntryNotFoundException`
- ⏳ `EntryExistsException`
- ⏳ `CqException` 系列
- ⏳ `FunctionException`
- ⏳ `TransactionException` / `RollbackException`
- ⏳ `CacheWriterException` / `CacheLoaderException` / `CacheListenerException`
- ⏳ 其他(`AlreadyConnectedException` / `InvalidDeltaException` / `DuplicateDurableClientException` …)

