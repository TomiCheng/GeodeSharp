# GeodeSharp — Implementation Progress

> 每個 phase 完工 / 開工時更新此檔。
> `CLAUDE.md` 是計畫（不變動），此檔是進度（會變動）。
> [PORTING.md](PORTING.md) 是 cppcache ↔ C# class 對應表（更細粒度的實作狀況）。
>
> **新會話 / 新 phase 銜接**：先讀本檔再決定要不要探索程式碼。

---

## Phase 0 — DI + entry interfaces ✅

- [x] 入口介面殼：`IGeodeCache` / `IRegion<TKey,TValue>` / `IQueryService` / `IQuery<T>` / `IGeodeCacheFactory`
- [x] `GeodeException`（BCL exceptions 用於 transport / 參數誤用；`GeodeException` 用於 Geode 協定失敗）
- [x] `GeodeClientOptions` + 子 options（cppcache 移植版，schema 尚待精簡）
- [x] `AddGeodeClient` 三個 overload（host config / 自帶 IConfiguration / Action delegate）× named & unnamed
- [x] `IGeodeCacheFactory` + `GeodeCacheFactory`（per-cache `AsyncServiceScope`、`Lazy<T>` 防競態、cascading async dispose）
- [x] `GeodeCache.EnsureInitializedAsync` 用 `Lazy<Task>(ExecutionAndPublication)`
- [x] 130 unit tests 通過、build 0 warning

**留待後續 phase 處理**（不算 Phase 0 漏項）：

- `IRegion` / `IQueryService` / `IQuery` 仍是空殼（無方法）— Phase 1.2 / 1.4 補上
- `GeodeClientOptions` 是 cppcache `SystemProperties` 全鏡像版（含 `LogOptions` / `StatisticsOptions` / `HeapOptions` / `CacheXmlOptions` / `ThreadPoolSize` / `EnableChunkHandlerThread` 等）— **這是刻意的**，依 CLAUDE.md「mirror then prune」政策，等 Phase 1.5 後期 / 釋出前才審視哪些保留
- 各 options 子類的 XML doc 需逐步補足 cppcache 來源（消費檔案 / 語意 / 平台限制），對齊 CLAUDE.md「Document semantics on the property」原則
- 缺 `AuthOptions` — Phase 3 安全工作再加

---

## Phase 1.1 — 建立單一伺服器連線 ✅

**目標**：透過 `Cache` 公開 API（`EnsureInitializedAsync` / `CloseAsync`）端到端開一條 server connection、跑 handshake、能送 Ping、優雅關閉。**不**做 pool、**不**做多 endpoint、**不**做 failover。

### Foundation（已完成 — protocol layer）

- [x] `BigEndianBinaryReader` / `BigEndianBinaryWriter`（unit tested）
- [x] `TcrPart` / `TcrMessage` / `TcrPartBuilder` / `TcrMessageBuilder`（unit tested）
- [x] `ClientProxyMembershipIdBuilder`（unit tested）
- [x] `MessageType` enum
- [x] `TcrConnection` 框架 + handshake bytes
- [x] `PingIntegrationTests` 對 `apachegeode/geode` 真機通過

### 接到 Cache

- [x] `TcrEndpoint.CreateNewConnectionAsync` 實作 — 開 socket、跑 handshake、回 `TcrConnection`
- [x] `Cache.InitializeCoreAsync` 從 options 拿單一 host:port → 建 `TcrEndpoint` → 呼叫 `CreateNewConnectionAsync`（commit `20a53fc`）
- [x] `Cache.CloseAsync` 送 `CloseConnection(18)` 並釋放連線
  - `TcrMessageBuilder.CloseConnection(bool keepAlive)` partial（1-byte payload，cppcache `TcrMessageCloseConnection` 對齊）
  - `TcrConnection.CloseAsync(keepAlive, ct)` — fire-and-forget 送 18 + 2s send budget + catch+LogInformation + `DisposeAsync`
  - `ThinClientPoolDM.DestroyAsync` Step 5a：drain `_opConnections` → 對每條 conn 呼叫 `CloseAsync(_keepAlive, ct)`
  - `_keepAlive` 欄位（cppcache `m_keepAlive` 鏡像；`DestroyAsync(bool keepAlive)` 寫入）；Phase 1.1 永遠 false
  - **TODO Phase 1.5**：`_endpoints` 釋放 TCCM ref（`ConnManager.RemoveRefToTcrEndpointAsync`），目前靠 cache scope dispose 連鎖收尾
- [x] `EnsureInitializedAsync` 之後 ping loop 端到端能跑
  - `ThinClientPoolDM.PingLoopAsync` + `PingServerLocalAsync`（commit `07820de`）
  - `TcrEndpoint.PingAsync(ThinClientPoolDM, ct)` 對齊 cppcache `pingServer`（含 `_msgSent` / `_pingSent` 短路）
  - `ThinClientBaseDM.SendSyncRequestAsync` / `SendRequestToEndpointAsync` 簽名收成 `TcrMessage` → `Task<TcrMessage>`（不再 by-ref reply + GfErrType code）
  - `ThinClientPoolDM.SendRequestToEndpointAsync` + `GetFromEPAsync` + `CreatePoolConnectionToAEndPointAsync` + `PutInQueueAsync` Phase 1.1 切片
  - 整合測試 `PingLoop_pings_endpoint_against_real_server`（commit `bc6b909`）— 配 `MinConnections=1` / `IdleTimeout=100ms` / `PingInterval=200ms`，驗 `PingTickCount>=3` && `PingSuccessCount>=2` && `PoolSize>=1`
- [x] **（Phase 1.1 收尾）** Options 驗證 + per-cache scope 架構整理
  - 新檔 [`Internal/GeodeClientOptionsValidator.cs`](src/Geode.Client/Internal/GeodeClientOptionsValidator.cs) — `IValidateOptions<GeodeClientOptions>`，accumulate failures：`Pools.Count >= 1` / `Pool.Name` 非空白 / `Locators+Servers >= 1` / `CacheXmlHostPort.Host` 非空 + `Port ∈ [1, 65535]` / `MinConnections >= 0` / `MaxConnections >= MinConnections`
  - 三個 `AddGeodeClient` overload 串 `.ValidateOnStart()`；`AddCore` 用 `TryAddEnumerable<IValidateOptions<>>` 註冊 validator（additive 語意 + 多 cluster 不重覆）
  - 新檔 [`Internal/CacheScopeContext.cs`](src/Geode.Client/Internal/CacheScopeContext.cs) — per-scope holder（`Name` + `Options` + 一次性 `Initialize`）。解掉 `IOptions<T>.Value` 永遠回 default name 的架構錯位（named-only 註冊下 `ClientProxyMembershipIdBuilder` / `TcrConnection` 之前都讀錯 options）
  - 所有 per-cache 「唯一一個」的服務改 Scoped：`Cache` / `TcrConnectionManager` / `PoolManager` / `ClientProxyMembershipIdBuilder` / `CacheScopeContext`。`GeodeCacheFactory.Build` 簡化成「建 scope → `Initialize(name, options)` → `GetRequiredService<Cache>()`」；`DisposeAsync` 只 dispose scope，cascade 連鎖 dispose 全部 scoped service
  - 「每 scope N 個動態實例」的型別（`ThinClientPoolDM` / `TcrEndpoint` / `TcrConnection`）保留 `ActivatorUtilities` — DI Scoped 是 exactly-one，不適用

**下一步入口**：Phase 1.2 — Single-key CRUD。

### 後移到別的 phase

| 原 Phase 1.1 項目 | 移到 |
|---|---|
| Built-in DSFID 型別 codec（string / byte[] / 各 primitive / collection） | **Phase 1.2** — Put/Get 才實際需要序列化 |
| `PutGetIntegrationTests` / `GetDiagnosticTests` 五個 Skip | **Phase 1.2** — 是 Put/Get 的整合測試 |
| 多 endpoint / failover / pool | **Phase 1.5** |

---

## Phase 1.2 — Single-key CRUD ✅（int32 KV walking-skeleton）

**目標**：`IRegion<int,int>` 的 4 個基本 op（Put / Get / Remove / ContainsKey）端到端通過真實 Apache Geode server。CRUD 完備之後就有第一個 demo-able milestone。

### Region lookup 路徑

- [x] `IRegionService.GetRegion(string)` / `GetRegion<TKey,TValue>(string)` interface 殼（lookup-only，找不到回 null，對齊 cppcache `CacheImpl::getRegion`）
- [x] `Cache.GetRegion(string)`（untyped）實作完成 — line-for-line 對齊 cppcache `CacheImpl::getRegion` (`CacheImpl.cpp:475-518`)：throwIfClosed / `_destroyPending` / 空字串 / `"/"` 驗證 / leading-slash strip / first-segment lookup ；sub-region 路徑（中間有 `/`）目前 NIE，留 sub-region phase
- [x] `Cache.GetRegion<TKey,TValue>(string)` typed overload — `region is null ? null : new RegionView<TKey,TValue>(region)`
- [x] `RegionView<TKey,TValue>` typed wrapper（[Services/RegionView.cs](src/Geode.Client/Services/RegionView.cs)）— compile-time-only typed view，每次 `GetRegion<K,V>` 都 new 一個；K/V 純編譯期保護，runtime 不追蹤；型別錯靠 unbox 自然噴 `InvalidCastException`
- [x] `IRegion` 加 `Name` / `FullPath` / 4 個 `object`-typed op；`IRegion<TKey,TValue>` 加 4 個 typed overload（無 `new` 修飾，純 overload）
- [x] `RegionInternal` / `LocalRegion` / `ThinClientRegion` 三層空殼建立（鏡像 cppcache `Region → RegionInternal → LocalRegion → ThinClientRegion`）
- [x] `Cache.InitializeCoreAsync` 從 `CacheXml.Regions` 預建 `ThinClientRegion` 寫入 `_regions`（含 refid 模板解析；commit `c830494`）

### Serialization

- [x] `Protocol/Serialization/IDataConverter` + 泛型版 + `SerializationRegistry`（per-cache Scoped；DSCode ↔ converter 雙向索引；`WriteObject` / `ReadObject` 中央 dispatch；對齊 cppcache `SerializationRegistry`）
- [x] `Int32DataConverter`（DSCode `CacheableInt32` = 57，4-byte BE）
- [x] `BooleanDataConverter`（DSCode `CacheableBoolean` = 53，1-byte）
- [x] `EventIdGenerator`（Scoped；`ThreadId=1` 常數 + 實例 seq；對齊 cppcache `EventIdTSS` instance scope，不能 static — 詳見「踩過的坑」）

### Wire 訊息 + region op 實作

- [x] `Put(7)` / `Request(0)` / `Destroy(9)` / `ContainsKey(38)` 全部走 `SerializationRegistry`（key / value / callbackArgument 一致路徑；no inline type guards）
  - [Protocol/TcrMessageBuilder.Put.cs](src/Geode.Client/Protocol/TcrMessageBuilder.Put.cs)
  - [Protocol/TcrMessageBuilder.Get.cs](src/Geode.Client/Protocol/TcrMessageBuilder.Get.cs)
  - [Protocol/TcrMessageBuilder.Destroy.cs](src/Geode.Client/Protocol/TcrMessageBuilder.Destroy.cs) — `value=null, isUserNullValue=false` 分支（unconditional destroy）；conditional `remove(key, value)` 留以後
  - [Protocol/TcrMessageBuilder.ContainsKey.cs](src/Geode.Client/Protocol/TcrMessageBuilder.ContainsKey.cs)
- [x] `ThinClientRegion` 4 個 op 全部 end-to-end：
  - `ContainsKeyAsync` — Response part 0 → `bool`（commit `23f9f73`）
  - `PutAsync` — Reply OK / Exception
  - `GetAsync` — Response part 0 via `SerializationRegistry.ReadObject`（含 cppcache `readObjectPart` 對應的 lenObj/isObj 4 種情況：missing key → null）
  - `RemoveAsync` — Reply 最後一個 part 讀 entryNotFound i32（Phase 1.2 沒 versionTag 所以最後一個 part 一定是 entryNotFound；versionTag 落地時改順序解析）

### 踩過的坑（cppcache scope parity）

**Symptom**：`RegionCrudIntegrationTests` 第一次跑 3/5 過、2/5 fail — Put 看似成功（無 exception），但 Get 回 0、ContainsKey 回 false，像 server 把 Put 默默吃掉。

**Root cause**：`ClientProxyMembershipIdBuilder.s_uniqueTag` 我寫成 `static readonly`（process-wide singleton），但 cppcache `ClientProxyMembershipIDFactory::randString_` 是 **instance member**（每個 `CacheImpl` 一份）。同 process 內兩個 `Cache` 共用 clientId → 加上各自 `EventIdGenerator` 從 seq=1 開始 → server 的 `ClientHealthMonitor` 把 `(clientId, threadId=1, seq=1)` 第二次出現視為 duplicate event **靜默丟棄**。

**Fix**：
- [Protocol/ClientProxyMembershipIdBuilder.cs](src/Geode.Client/Protocol/ClientProxyMembershipIdBuilder.cs) — `s_uniqueTag` → `_uniqueTag` (instance field, ctor 生)
- [Internal/EventIdGenerator.cs](src/Geode.Client/Internal/EventIdGenerator.cs) — `_sequenceId` 維持 instance（uniqueTag per-cache 之後 clientId 跨 cache 不同 → seq 跨 cache 從 1 重來不會撞）

教訓寫進 [memory/cppcache-scope-parity.md](C:\Users\c_tom\.claude\projects\D--projects-tomi-GeodeSharp\memory\cppcache-scope-parity.md)：bucket-2 cppcache class 每個欄位的 `instance` / `static` / `thread_local` 都要鏡像，不要自作主張 optimize 成 static。

### 測試

- [x] Unit tests — 161/161 通過（含 `TcrMessageBuilderGetTests` / `PutTests` / `DestroyTests` 全部改成 int32 KV，外加 `ClientProxyMembershipIdBuilderTests` 加上 per-cache uniqueTag 驗證）
- [x] [RegionCrudIntegrationTests](tests/Geode.Client.IntegrationTests/RegionCrudIntegrationTests.cs) — 5 個 case（Put→Get、Get missing、ContainsKey 軌跡、Remove missing、Put 覆蓋）全綠對 `apachegeode/geode` 真機，含 3s `FreshConnectionSettleDelay` 防 cold-container race

### Deferred / 留待後續

- Built-in DSFID 型別 codec 擴充（string / byte[] / int64 / int16 / byte / float / double / DateTime / null / List / Dictionary / array / HashSet）— int32 + bool 已落地，其他 codec 等真的有 demo 需要時再補
- `PutGetIntegrationTests` / `GetDiagnosticTests` 五個 Skip — 是上 phase 用 byte[]/string 經由 raw `TcrConnection.SendRequestAsync` 的舊測試，等 String / Bytes codec 落地或乾脆刪掉（已被 RegionCrudIntegrationTests 涵蓋大半）
- `RegionView` 跟 `IRegion` op 殼的 unit test 還沒寫（行為已被整合測試蓋到，補 unit 是 nice-to-have）
- **`callbackArgument` overload**：cppcache `Region::put/get/destroy` 都收 `aCallbackArgument`（forward 給 server 端 CacheListener / CacheWriter / CacheLoader / PartitionResolver）。`TcrMessageBuilder.*` 已經接這個欄位（wire 對齊），但 `IRegion` / `IRegion<TKey,TValue>` 還沒暴露。等真的有需求或要對齊 cppcache public surface 時，加 overload：
  - `PutAsync(key, value, object? callbackArgument, CancellationToken)`
  - `GetAsync(key, object? callbackArgument, CancellationToken)`
  - `RemoveAsync(key, object? callbackArgument, CancellationToken)`
  - `ContainsKey` 不加（cppcache `containsKeyOnServer` 也沒收 callback）
  影響範圍：`IRegion` / `IRegion<TKey,TValue>` / `RegionInternal`（把 callback 版設 abstract、no-callback 版 forward 過去）/ `ThinClientRegion`（callback 改 canonical 實作）/ `RegionView`（typed + 顯式 IRegion 兩組 overload）。Builder 端不用動。
- Fresh-conn race（[memory](C:\Users\c_tom\.claude\projects\D--projects-tomi-GeodeSharp\memory\geode-fresh-conn-race.md)）— 用 `Task.Delay(3s)` 在測試端規避；正式 fix（pool warmup / readiness probe）留給 Phase 1.5

**下一步入口**：Phase 1.3 — Bulk + management ops（PutAll / GetAll70 / RemoveAll / Clear / Invalidate）。

---

## Phase 1.3 — Bulk + management ops（未啟動）

- [ ] `PutAll(56)` / `GetAll70(100)` / `RemoveAll(109)`
- [ ] `Clear`
- [ ] `Invalidate`

---

## Phase 1.4 — OQL Query（未啟動）

- [ ] `IQueryService.NewQuery<T>(oql)` / `IQuery<T>.ExecuteAsync(ct)` 介面
- [ ] `Query(34)` 訊息與結果解碼（`SELECT *` → `IReadOnlyList<TValue>`、`SELECT COUNT(*)` → `long`）
- [ ] Region convenience：`ExistsValueAsync` / `SelectValueAsync`

---

## Phase 1.5 — Connection management（未啟動）

- [ ] Connection pool 設計（cppcache `ThinClientPoolDM` 為參考；先決定 `MaxConnections` 是 pool-wide 還是 per-endpoint）
- [ ] `PoolOptions` 審視：哪些 cppcache 欄位保留 / 改名 / 刪除（依 CLAUDE.md「mirror then prune」，此階段才處理）
- [ ] Locator 線路協定（與 server 不同）
- [ ] Multi-server failover、自動重連
- [ ] Server endpoint 健康監控

---

## Phase 2+ — Custom objects、安全、效能、分片

詳見 [CLAUDE.md](CLAUDE.md) Phase 2 / 3 / 4。
