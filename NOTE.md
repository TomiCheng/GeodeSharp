# NOTE

> 從 `PROGRESS*.md` 整理出來的長期參考內容。專案進度本身看 source
> 裡的 NIE / TODO 分佈,不再維護 phase 清單;這份檔留的是「跨時段
> 仍有用」的知識:scope、wire protocol 參考、跟 cppcache 偏離的決
> 策、未做完的 cleanup、未實作功能的設計初稿。

---

## 刻意不做的東西(專案 scope)

- **`cache.xml`** — 改用 `appsettings.json` + `IOptions<T>`。
- **Sub-regions** — Geode 自己也勸退。
- **Sync API** — async only。
- ~~**Cache listener / loader / writer**~~ — **已從 scope 移回(2026-05)**:純
  managed user hook,無 wire / subscription 相依即可運作。`ICacheLoader`
  read-through 與 `ICacheListener` after-event dispatch 已實作 + 測試;
  `ICacheWriter` interface / setter 在但 dispatch 未接。覆蓋狀態見
  PORTING.md「Cache callbacks」段。
- **Region expiration / eviction** — server-side 管,client 不碰。
- **Non-pool 路徑** — `ThinClientDistributionManager` /
  `TcrDistributionManager` / `TcrHADistributionManager` 整個 sub-tree
  刻意不 port。對齊現代 Geode 推薦慣例;要無 pool 行為就配 default
  pool。詳見 memory `pool-only-no-non-pool.md`。

---

## Wire protocol 參考

### Frame layout(big-endian)

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

### Handshake

**不**走標準 frame format,是 ad-hoc byte sequence。翻譯
`cppcache/src/TcrConnection.cpp::sendHandshakeForServer` 要 byte-by-byte。
**不要憑記憶寫。**

### MessageType

canonical list 在 `src/Geode.Client/Protocol/MessageType.cs`(鏡像
cppcache `cppcache/src/TcrMessage.hpp`)。

---

## Options 政策

1. **Mirror first, prune later。** 移植 cppcache config knob 時把每個欄
   位都帶過來(一個 C# property 對一個 cppcache key,default 對齊
   cppcache 常數)。Prune 一次到位、留到晚 — pre-release audit 時對
   著「實際讀這欄位的程式路徑」逐個審。移植當下不要憑直覺判斷哪個
   「看起來沒用」。
2. **語意寫在 property 上,不是側邊筆記。** 每個 options property 的
   XML doc 紀錄從 cppcache 讀到的事:哪個檔案吃這個值、它真正驅動什麼
   (例:`SO_SNDBUF` / expiry-task interval / per-endpoint cap)、層級
   (pool / connection / endpoint)、平台限制(例:`#ifdef __linux`)。
   六個月後 review 這個欄位的人不該需要回頭讀 cppcache。
3. **不要為還沒實作的東西發明 JSON schema。** 具體 JSON 形狀逐個對著
   cppcache `SystemProperties` 語意決定;不寫沒程式碼支撐的目標 schema。

---

## 與 cppcache 偏離的設計決策

跨時段都會踩到,記下來省得每次都得重新 reason:

- **Endpoint 階層收一個** — cppcache 拆 `TcrEndpoint`(非 pool 基類)
  + `TcrPoolEndPoint`(pool 子類,持單一 `m_dm`);我們合一,改用
  `_distMgrs` list 支援多 pool 共用 endpoint。空殼 `TcrPoolEndPoint`
  已建,真正 migration 沒完成。
- **Endpoint 跨 pool 共用** — cppcache 每個 pool 自己一份 endpoint
  實例(同 host:port 多 instance);我們 TCCM 全域唯一一份,多 pool
  共用。代價是「endpoint 的擁有 DM 是誰」要 list 處理,失去 1:1
  確定性。
- **`SetConnected` 廣播 vs 單通知** — cppcache 只通知 `m_baseDM`,
  我們走 `_distMgrs` 全廣播。代價:callee(`Inc/DecConnectedEndpoints`)
  必須 lock-free 且 non-reentrant。
- **Non-pool 路徑收掉** — 見上方 scope。
- **Stats Counter+Time pair 合成單一 Histogram** — cppcache 多處用兩
  個獨立欄位(`IntCounter` 次數 + `LongCounter` ns 累計時間),我們
  合成單一 `Histogram<double>`(秒)。`.Count` = 原次數、`.Sum` = 原
  累計時間。已套用:`LocatorListRequestTime` /
  `ClientConnectionRequestTime` / `ConnectionWaitTime` / `ClientOpTime` /
  `LoaderCallTime` / `ListenerCallTime`。
- **`IsDeltaEnabledOnServer` instance 而非 static** — cppcache 用
  process-wide `static volatile s_isDeltaEnabledOnServer`;我們改 per-DM
  `virtual` instance property。理由:多 `IGeodeCache` 連不同 cluster 時
  各自追自己的 delta capability,且對齊 DI-first / no static singleton。
  目前回 `false`(handshake 還沒寫入),delta send path 因此不啟動。
- **`getNoThrow` 的 putLocal-failure oldValue fallback 收不回** —
  cppcache `putLocal` 用 out-param 即使回 error code 也帶 `oldValue`;
  我們的 `PutLocalAsync` 是「回 oldValue **或** 丟 `GfErrTypeException`」
  二選一,拋了就拿不到。所以 cppcache 「other error → `value = oldValue`」
  那條(`LocalRegion.cpp:999-1007`)無法忠實複製,只能 keep fetched
  value。Phase 1.x 不可達(concurrency checks 未驅動);等 entries map
  能回 race-loser value 再修。

---

## 待清理的 dead-code / prune 清單

下列「audit 完成、刪除動作未做」的清單。動工時就把對應的 source 處
加 `// TODO:` 內聯,然後從這個清單移掉。

### Options-tree prune
- **`HeapOptions`** — server-side 概念(`heap-lru-limit` /
  `heap-lru-delta` / `tombstone-timeout`),沒 client 對應;只被
  `GeodeClientOptions.Heap` + clone/validate 引用。
- **`PoolOptions` system-properties 層(5 個欄位)** —
  `ConnectionPoolSize`(per-EP cap 未實作)、`ConnectWaitTimeout`
  (Linux EPIPE workaround,.NET async sockets 無關)、
  `MaxSocketBufferSize`(從未套到 socket)、`ShuffleEndpoints`
  (我們 DM 用 `Random.Shared.Next` at construction)、
  `BucketWaitTimeout`(PR routing,未來)。
- **`GeodeClientOptions` 根層(2 個)** — `ThreadPoolSize` /
  `EnableChunkHandlerThread`(xmldoc 自己承認「在 .NET 下很可能是
  no-op」;後者在 `ThinClientBaseDM.cs:66` 有一個 stale TODO marker)。
- **`CachePoolOptions` per-pool 層(4 個)** — `SocketBufferSize`
  (跟 `PoolOptions.MaxSocketBufferSize` 重複)、`Subscription{AckInterval,
  MessageTrackingTimeout,Redundancy}`(subscription / CQ 上線時再加回)。
- **`CacheOptions` cache 層(2 個)** — `RedundancyLevel`(subscription
  redundancy 上線時再加回)、`Version`(寫死 `"1.0"`,從未驗證)。
- **保留(已知有 consumer,不要刪)** —
  `CachePoolOptions.MultiuserAuthentication`(security 階段;
  `_isMultiUserMode` 已讀)、`SubscriptionEnabled`(subscription 階段
  `ThinClientPoolHADM` factory selector)、`ThreadLocalConnections`
  (sticky factory selector)、`PingInterval`(刻意 nullable 給
  `xmlPool.PingInterval ?? options.Pool.PingInterval` 兩層 fallback)。

### Lifecycle dead-code
- **TCCM dead-code 移除** — 6 個 NIE methods + dead fields + `InitAsync`
  的 `isPool` 參數可以刪;簡化後重寫 class XML doc 反映真實角色
  (「endpoint registry + durable flag holder」)。盤點完約 80 行刪、
  10 行修改,動工未做。
- **`ConnManager.RemoveRefToTcrEndpointAsync`**(release TCCM endpoint
  refs)— 目前靠 cache-scope dispose cascade 帶走。

### Deferred test
- **`PutInQueueAsync` destroyed-guard 測試** — `_isDestroyed` guard
  (cppcache `ConnectionQueue::put` `closed_` 分支,
  `ConnectionQueue.hpp:62-67`)實作了但沒測。Happy path 被
  `CacheConnectionIntegrationTests` / `RegionCrudIntegrationTests`
  的 back-to-back op 間接覆蓋。destroyed-guard 本身從 public API 結構
  上不可達(`SendRequestToEndpointAsync` 進來就擋 `_isDestroyed != 0`),
  只在 `SendRequestToEndpointAsync` 中段的 race 窗才會觸發。要做
  deterministic test 需要 (a) integration test 編排 wire response 在
  `SendAsync` 暫停時 race `DestroyAsync`,或 (b) 放寬可見性 + DI 樹
  scaffold + spy 一個 `sealed` `TcrConnection`。兩種對 5 行 guard 都
  cost-ineffective。等 `PoolDisconnects` Meter 或 socket-leak 工具上線
  (那時 guard 就有可觀測的對應物)再回頭。Source 處有 inline comment
  flag 這件事。

### 其他已知未完
- **`PoolStatistics` 剩 7 個 catalogue 欄位** — `subscriptionServers`
  (HA)、`messagesBeingReceived`(notification channel)、
  `processedDelta*` × 3(delta propagation)、`queryExecutions` /
  `queryExecutionTime`(query 路徑已存在,stat 沒接)。
- **`TcrEndpoint` / `TcrPoolEndPoint` 階層遷移** — skeleton 已建
  (commit `7be777c`),真實切換未做。
- **Auth-trio throw site** — `AuthenticationFailedException` /
  `AuthenticationRequiredException` / `NotAuthorizedException` 三個
  class 存在但無人 throw;預定接在 handshake step 9
  (`acceptanceCode != REPLY_OK`)。
- **Ping timeout tolerance** — cppcache 容忍一次 timeout 才翻
  `connected` bit;我們任何例外都直接翻。要等完整 `GfErrType`
  taxonomy port 才能精確區分 transport timeout 與 server returned
  exception。
- **Fresh-conn race** — server-side `ClientHealthMonitor` 註冊延遲
  (cold JVM 5-100ms);測試靠 `FreshConnectionSettleDelay = 3s` 繞過。
  Memory `geode-fresh-conn-race.md` 紀錄為 server 端時序問題,client
  側改進可行性低。

---

## 未實作功能的設計初稿

### Heap-LRU entry sizing(local cache size 帳)

純記憶體 local cache 的真正想要的 bound 是 heap LRU(看記憶體用量),
entry-count(`_map.Count > LruEntriesLimit`)擋不住「1000 筆大 blob」。
難點:Local 存活的是 CLR 物件(不序列化),.NET 又沒便宜又準的
per-object size。

**cppcache 怎麼做:**
`Serializable::objectSize()` 每型別自報(default 回 0,「只有用 HeapLRU
才需實作」);`LRUEntriesMap` put 時算差量累加到 atomic
`m_currentMapSize`,`processLRU()` 超標就從 `lru_queue_` 踢最舊。size
**不存在** `MapEntry`,evict 時重算。PDX 好算是因為 `PdxInstanceImpl`
握著序列化 `buffer_`,size ≈ `buffer_.size()`。

**我們打算走的 sizing spec**(取代「每型別 objectSize 契約」):

`SerializationRegistry` 提供 **length-only pass** — counting / measure-mode
的 `DataOutput`(`GetSpan` 回收 scratch 不長大,`WrittenCount` 照累加),
跑既有 `WriteObjectAsync` 走訪但不產生 bytes,回 `WrittenCount`。
三路 dispatch:

- `IPdxSerializable` → 型別自報 `ObjectSize()`;
- 被 `IPdxSerializer` 處理 → `serializer.ObjectSize(obj)`;
- 一般有 converter 的型別 → counting pass;
- 以上都不是(沒 converter)→ 0(對齊 cppcache「回 0 = 不參與 heap 控管」)。

**跟 cppcache 的差異 / 我們的優化:**
長度 **cache 在 `MapEntry`**(put 算一次,evict 直接讀,不像 cppcache
重算);running total 放 `LRUEntriesMap` 的 counter(對齊
`m_currentMapSize`)。

**估算精度:** heap 帳不用 byte 級精準(cppcache `objectSize` 也是估)。
直覺值:`string` ≈ `Length*2`、`int[]` ≈ `Length*4`、`byte[]` ≈
`Length`(+ DSCode tag / 長度前綴幾 bytes)。

**更深的取捨:** heap 帳便宜的前提是「值以序列化形式存著」(PDX 握
buffer 即如此)。若要 heap LRU 對所有型別都便宜,真正的問題是
**Local 要存活物件還是 blob** — 存 blob → size = blob 長度(免費)但
get 要反序列化。counting pass 是「不改儲存策略也能量長度」的折衷。

**觸發設定兩條:** entry-count(`RegionAttributes.LruEntriesLimit`,
per-region,有 factory setter)vs heap(`SystemProperties.HeapLRULimit`,
全 cache,`appsettings` 的 `Heap.LRULimit`)。後者 `LRUEntriesMap`
ctor 收了 `heapLRUEnabled` 但目前未讀(CS9113)。先做 entry-count +
`LOCAL_DESTROY`,heap LRU 照本 spec 後排。

### Subscription / Continuous Query(v1 設計取向)

v1 不要求耐 server 失敗 — server 死掉訂閱也死掉,等價於 single-server
connection。Reconnect / 事件 replay 屬於 HA 升級。

cppcache 對應:`TcrEndpoint::registerDM(clientNotification=true)`、
`m_notifyConnection` / `m_notifyReceiver` task、
`CreateNewConnectionAsync(isClientNotification: true)` 路徑(目前 NIE)。

訂閱事件的 async 形狀 — cppcache `CacheListener` 是 sync callback;
.NET 慣例傾向 `IAsyncEnumerable<RegionEvent>` 或 `Channel<T>` 推送,
讓 listener 可以 await。決策推到實作時。

### PDX 對 .NET reflection 的依賴

`PdxTypeRegistry` 需要從 user-defined class 提取欄位 metadata。
考慮 source generator(`[PdxSerializable]` attribute → compile-time
emit)避開 reflection 性能成本。

### Locator follow-ons

cppcache `ThinClientLocatorHelper` 4 個 public method,實作了前兩個
(`UpdateLocators` / `GetEndpointForNewFwdConn`);剩下兩個待 port:

- `getEndpointForNewCallBackConn` — 訂閱通道專用 endpoint 選擇
  (避開主 op 通道)。Continuous Query 會直接相依。
- `getAllServers` — PR single-hop bucket-to-server resolution 用。
- `ClientReplacementRequest` — failover swap(locator 在某 endpoint
  倒掉時挑替代 server)。
