# Phase 1 — MVP 階段紀錄

> Phase 1 (1.1 - 1.5) 走的是 walking skeleton — connect / CRUD / bulk /
> query / pool / locator / failover 一路打通,對接真實 Apache Geode
> 叢集。本檔將整個 Phase 1 視為一個整體,不再細分子階段,只列已完
> 成與未完成項目。詳細歷史請看 git log。

---

## 已完成的項目

- **Cache 入口 / DI 接線** — `IGeodeCache` lifecycle
  (`EnsureInitializedAsync` / `CloseAsync`);handshake + auth-mode-NONE;
  `services.AddGeodeClient(...)` 三種 overload(host config / 外部
  `IConfiguration` / `Action` delegate)× 具名與不具名;
  `IGeodeCacheFactory` + per-cache `AsyncServiceScope` + 連鎖 async
  dispose;`IOptions<GeodeClientOptions>` 配置。

- **單筆 KV 操作** — `Put` / `Get` / `Remove` / `ContainsKey` 完整
  round-trip,對接 server 上的真實 region。

- **批次 KV 操作** — `PutAll` / `GetAll` / `RemoveAll` / `Clear` /
  `Invalidate`,單一往返處理多筆。

- **Region 便利查詢** — `ExistsValue` / `SelectValue` 包裝 OQL,單
  predicate 場景的甜蜜路徑。

- **內建型別序列化** — `byte[]` / `string`(modified-UTF-8 + UTF-16 大
  字串)/ 整型 / 浮點 / `DateTime` / `bool` / `decimal`;集合型別
  `List<T>` / `Dictionary<K,V>` / `HashSet<T>` / 陣列,皆對齊 Java
  DSCode 來回不損失。

- **OQL 查詢** — `IQueryService.NewQuery<T>(oql)` 介面 + `IQuery<T>`;
  支援 `SELECT *` / `SELECT COUNT(*)` / 多欄位投影
  (`SELECT a, b` → `QueryStruct`);`ChunkedQueryResponse<T>` 完整
  chunked 解碼;NewQuery 型別保護(只接 BCL 與註冊型別,擋掉 PDX 與
  ORM 套用)。

- **連線池** — `ThinClientPoolDM` 含 `_opConnections` idle queue、
  `MinConnections` / `MaxConnections` / `IdleTimeout` /
  `LoadConditioningInterval` / `FreeConnectionTimeout`;pool 層
  cap + 每個 endpoint 層 cap(雙層 slot semaphore)。

- **Locator 探索** — `ThinClientLocatorHelper` 處理
  `LocatorListRequest` + `ClientConnectionRequest`;背景 locator-list
  刷新 loop;multi-locator + multi-server fixture 整合測試通過。

- **Server failover** — `SendSyncRequestCoreAsync` 內 DM-level retry
  frame(Step A-G 對齊 cppcache);transport error 第一輪分類
  (`IsRetryableTransportError`);`RemoveEPFromMetadataIfError` 接在
  catch path;`ServerFailoverIntegrationTests` 用 `gfsh stop server`
  測過真實 server 倒掉場景。

- **連線生命週期** — ping loop(periodic timer 驅動)、conn-management
  loop(clean-stale + restore-min)、sticky-conn 骨架、endpoint 健康
  監控(`SetConnected` 透過 `_distMgrs` 廣播 inc/dec)。

- **可觀測性** — cppcache `PoolStatistics` 27 個 catalogue 欄位完成
  20 個;額外加非 catalogue 的 `PingSweepTime` / `EndpointPingTime`
  ping 監測,以及 `ConnectedServers`(對外暴露 cppcache 內部
  `connected_endpoints_` 計數);每個 locator RPC 有
  `ActivitySource` span;ObservableGauge / Histogram / Counter 三種
  instrument 混用,跟 OpenTelemetry / Prometheus 相容。

- **測試基礎建設** — xUnit v3 + FluentAssertions;Testcontainers +
  Podman + `apachegeode/geode` 容器;`MeterCapture` test helper 抓
  instrument 量測;107 個整合測試(102 過、5 跳過、0 失敗)。

---

## 未完成的項目

- **PoolStatistics 剩 7 個 catalogue 欄位** — `subscriptionServers`
  (Phase 2+ HA)、`messagesBeingReceived`(Phase 2+ notification
  channel)、`processedDelta*` × 3(Phase 2+ delta propagation)、
  `queryExecutions` / `queryExecutionTime`(Phase 1.4 路徑已存在,只是
  stat 沒接);`PoolDisconnects` 未在每個 close site 都接到。

- **`TcrEndpoint` / `TcrPoolEndPoint` 階層遷移** — 空 skeleton 子類已
  建(commit `7be777c`),但真實切換沒完成。要做到 cppcache 全口徑
  「每個 pool 自己一份 endpoint 實例」需要把 endpoint 建構從
  TCCM 搬到 pool 自己的 `addEP`、`_distMgrs` list 改回單一 `_baseDM`、
  `conn.PoolDM` 可從 `endpoint.GetPoolHADM()` 推導。連帶問題:目前
  `conn.PoolDM` 在 handshake 完成後才 set,handshake 階段的 bytes 不算
  進 `ReceivedBytes`。

- **Auth-trio 真實 throw site** — `AuthenticationFailedException` /
  `AuthenticationRequiredException` / `NotAuthorizedException` class
  存在但無人 throw。Handshake step 9(`acceptanceCode != REPLY_OK`
  分支)是預定位置,等 Phase 3 security 做 cppcache `AUTH_REQUIRED` /
  `AUTH_FAILED` 對應。

- **Fresh-conn race 真正改進** — server-side `ClientHealthMonitor`
  註冊延遲(cold JVM 5-100ms);測試靠
  `FreshConnectionSettleDelay = 3s` 繞過。Memory
  `geode-fresh-conn-race.md` 紀錄這是 server 端時序問題,client 側
  改進可行性低。

- **TCCM dead-code 清理** — 6 個 NIE method + dead field +
  `InitAsync` 的 `isPool` ctor 參數可以刪;盤點完(約 80 行刪、
  10 行修改),動工未做。

- **Options-tree 修剪** — 已知無 consumer 的欄位:`HeapOptions`
  整個 class、`PoolOptions` system-properties 層 5 個欄位、
  `GeodeClientOptions` 根層 2 個欄位、`CachePoolOptions` 4 個欄位、
  `CacheOptions` 2 個欄位。Audit 完成,刪除動作未做。

- **PR single-hop / `ClientMetadataService`** — placeholder 在,
  `RemoveBucketServerLocation` 是 no-op stub;真實實作要等 Phase 4
  PR(partition routing)。

- **Ping timeout tolerance** — cppcache 容忍一次 timeout 才翻
  `connected` bit;我們任何例外都直接翻。要等完整 `GfErrType`
  taxonomy port 才能精確區分 transport timeout 與 server returned
  exception。

- **Non-pool 路徑去留決定** — `ThinClientDistributionManager` /
  `TcrDistributionManager` / `TcrHADistributionManager` 整個 sub-tree
  刻意未 port(memory `pool-only-no-non-pool.md`)。Pre-release audit
  時決定:真實作或徹底丟掉結構性 placeholder。

- **DI surface reshape** — `IGeodeCacheFactory` + extension 重整,
  planned 但未開工。

- **PORTING.md 持續更新** — cppcache 對映表的維護,新加 class
  (例如 `TcrPoolEndPoint`)的狀態欄、Bucket 1 / Bucket 3 對映新增。

---

## 設計決策與已知偏離

Phase 1 階段做了幾個跟 cppcache 偏離的設計選擇,記錄供 Phase 2+
或 pre-release audit 時回顧:

- **Endpoint 階層收一個** — cppcache 拆 `TcrEndpoint`(非 pool 基類)
  與 `TcrPoolEndPoint`(pool 子類,持單一 `m_dm`);我們合一,改用
  `_distMgrs` list 支援多 pool 共用 endpoint。空殼 `TcrPoolEndPoint`
  已建,migration 是未完成項目。

- **Endpoint 跨 pool 共用** — cppcache 每個 pool 各自一份 endpoint
  實例(同 host:port 多個 instance);我們 TCCM 全域唯一一份,多 pool
  共用。這是上一條的延伸,代價是「endpoint 的擁有 DM 是誰」需要
  list 處理,失去 1:1 確定性。

- **`SetConnected` 廣播 vs 單通知** — cppcache 只通知 `m_baseDM`,我
  們走 `_distMgrs` 全廣播。代價是 callee(`Inc/DecConnectedEndpoints`)
  必須 lock-free、non-reentrant。

- **Non-pool 路徑收掉** — cppcache 有完整非 pool sub-tree;我們強制
  pool 模式,使用者要無 pool 就配 default pool。對齊現代 Geode 推薦
  慣例。

- **Stats Counter+Time pair 合成單一 Histogram** — cppcache 多處用兩
  個獨立欄位(IntCounter +「次數」+ LongCounter ns「累計時間」),
  我們合成單一 `Histogram<double>`(秒)。`.Count` = 原次數、`.Sum`
  = 原累計時間。已套用:`LocatorListRequestTime`、
  `ClientConnectionRequestTime`、`ConnectionWaitTime`、`ClientOpTime`。

---

## Phase 1 階段交付的測試覆蓋率

- 161 個 unit 測試(`Geode.Client.Tests`)
- 107 個整合測試(`Geode.Client.IntegrationTests`)— 102 過、5 跳過
  (3 個調查中的 RegionDestroyed flake、2 個診斷用 dump)、0 失敗
- 整合測試對接的真實環境:`apachegeode/geode` 容器(via Podman)、
  multi-locator + multi-server fixture、`gfsh` 動態操作 server 起停
