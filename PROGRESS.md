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

**下一步入口**：Phase 1.3.c — PutAll(56) + GetAll70(100)。Chunked-reply 基建已在 1.3.b 落地，1.3.c 主要是新 wire 訊息 + `GetAll` 端 keys-section / objects-section 真路徑（1.3.b 已寫的 decoder 第一次被「真實 hasObjects」打到）。

---

## Phase 1.3 — Bulk + management ops

### 1.3.0 — `IDataConverter` 內建型別擴充 ✅

Phase 1.2 只實作 `Int32` + `Boolean` 兩個 converter；bulk ops 端到端整合測試要更有代表性的 K/V 型別。先把 MVP scalar / string / bytes 一次補齊，後面 1.3.a–1.3.c 都吃這個前置。

**完工狀態**：
- 11 個 Tier A converter src + unit tests + integration tests 全綠（292 unit + 17 integration）
- `IDataConverter` API 改造完成（`DsCodes[]` / `GetDsCode(value)` / `Write(w, v, dsCode)` / `Read(r, dsCode)`），cppcache `Serializable::getDsCode()` 對齊
- `IRegion<TKey, TValue>` constraint `where TKey : IEquatable<TKey>`（編譯期擋集合 / `byte[]` / 無 IEquatable POCO）
- 順手修了 `BigEndianBinaryReader.ReadArrayLen` signed/unsigned bug（phase 1.1 留下來的潛在問題，length 128..252 被誤判負數）

**後續補強**（1.3.0 落地之後分別追加的工作）：

- **B 路 server-side type verification**（commit `2854ce4`）— Put/Get round-trip 無法證明 server 真的把 wire bytes 解成對的 Java 型別（encoder/decoder 同向出 bug 抓不到）。透過 `docker exec gfsh get` 讀 server 端 `Value Class` + `Value` 斷言，補上這個盲點。13 個 fact 涵蓋全部 Tier A converter（String 四個 DSCode variant 各一 fact）。`GeodeFixture` 加 `GfshAsync` helper + 容器 TZ=UTC（DateTime / java.util.Date 顯示穩定）。**意外發現**：gfsh 印 `java.util.Date` 用 raw ms-since-epoch（非 `Date.toString()`），精度直達 ms 強於原本計劃的秒級驗證。
  - **byte[] B 路 deferred** — gfsh 對 byte[] 印 `[B@<identityHash>`，沒值可驗。Phase 2 Java sidecar 補。
- **Tier B-1 primitive arrays 落地**（src + unit tests 已完成、整合 + B 路驗證待加）— 詳見下方 Tier B-1 段落。
- **文件結構整理**（commit `ab1d030`）— CLAUDE.md 把 Bucket 1 / Bucket 3 對應表移到 PORTING.md、Phase 1 sub-phase 細節 / MessageType 表 / Public API 介面 code block / Phase 1.1 bootstrap prompt 全部移除（reference data 各歸其位、過期模板砍掉），CLAUDE.md 從 456 → 406 行。

**架構決策（已拍板）：**

`IDataConverter` 介面改造（cppcache `Serializable::getDsCode()` + `Serializable::toData` 對齊）：

```csharp
interface IDataConverter
{
    byte[] DsCodes { get; }                              // decode 用，多 DSCode 對應同一 converter（String 4 個）
    Type ManagedType { get; }                            // encode lookup 用
    byte GetDsCode(object value);                                    // encode 時依 value 內容回實際 DSCode
    void Write(BigEndianBinaryWriter w, object value, byte dsCode);  // payload only；dsCode 由 registry 傳回避免 String 掃兩次
    object? Read(BigEndianBinaryReader r, byte dsCode);              // payload only；registry 已讀掉 DSCode byte、再傳回供 String 分支
}
```

`SerializationRegistry` 改動：
- `Register` 改成 loop `converter.DsCodes` 把每個都掛進 `_byDsCode`
- `WriteObject`：`var dsCode = converter.GetDsCode(value); writer.WriteByte(dsCode); converter.Write(writer, value, dsCode);`
- `ReadObject` 流程不變（registry 仍負責讀 DSCode byte + dict lookup）
- Read / Write 對稱：兩邊都 registry 處理 DSCode byte、converter 只處理 payload

**Tier A — Phase 1.3.0 範圍（9 個 converter + String 一 converter 多 DSCode）：**

| DSCode | cppcache | CLR | 備註 | 狀態 |
|---|---|---|---|---|
| 53 | `CacheableBoolean` | `bool` | | ✅ Phase 1.2 |
| 54 | `CacheableCharacter` | `char` | UTF-16 code unit, 2-byte BE | [ ] |
| 55 | `CacheableByte` | `byte` | 故意用 unsigned（.NET 慣例），wire bit pattern 與 Java signed byte 互通；Java 端 -1 ↔ 我們 255 | [ ] |
| 56 | `CacheableInt16` | `short` | | [ ] |
| 57 | `CacheableInt32` | `int` | | ✅ Phase 1.2 |
| 58 | `CacheableInt64` | `long` | | [ ] |
| 59 | `CacheableFloat` | `float` | IEEE-754 BE, NaN/±∞ wire 形狀與 Java 一致 | [ ] |
| 60 | `CacheableDouble` | `double` | IEEE-754 BE | [ ] |
| 61 | `CacheableDate` | `DateTime` | 8-byte ms-since-epoch UTC. Read 回 `Kind=Utc`（偏離 clicache 的 `Local`，修 round-trip footgun）；Write `Utc` 直用 / `Local` → `ToUniversalTime` / `Unspecified` **throw `ArgumentException`**（拒絕沉默假設 Local，clicache bug 修正）；精度 truncate to ms | [ ] |
| 46 | `CacheableBytes` | `byte[]` | VL-encoded length + raw bytes（1/3/5 byte prefix）；`null` 走 NullObj、`byte[0]` 走 DSCode 46 + length=0；**不可當 Key**（`Array` 不實作 `IEquatable<T>`、cppcache `CacheableArrayPrimitive` 不繼承 `CacheableKey`，編譯期被 `where TKey : IEquatable<TKey>` 擋掉）；**順手修了 `ReadArrayLen` signed/unsigned bug**（length 128..252 範圍原本被誤判為負數） | ✅ |
| 42 / 87 / 88 / 89 (+69 read-only) | `CacheableString` / `…ASCIIString` / `…ASCIIStringHuge` / `…StringHuge` (+`CacheableNullString`) | `string` | 一 converter 多 DSCode；ASCII vs modified UTF-8 × short(u16) vs huge(u32) — 但 huge UTF 路徑用 **UTF-16 BE** 不是 modified UTF-8 huge（對齊 cppcache `writeUtf16Huge`）；69 是 read-only null sentinel；`BigEndianBinaryReader.ReadJavaModifiedUtf8` 從 stub 補成實作 | ✅ |

**Tier B-1 — primitive arrays ✅（後續補強）**

8 個 converter src + 62 unit tests 落地（unit total 323 → 385）。Wire 形狀：`WriteArrayLen` 1/3/5 byte VL prefix + N × 元素位元（primitive raw bytes / `string[]` 每元素自己的 DSCode+payload）。整合測試 + B 路驗證仍待加。

| DSCode | cppcache | CLR | 備註 |
|---|---|---|---|
| 26 | `BooleanArray` | `bool[]` | VL length + N×1 byte；decode tolerant 任何非 0 byte = true |
| 27 | `CharArray` | `char[]` | VL length + N×u16 BE（Java `char[]`，不是 UTF-8） |
| 47 | `CacheableInt16Array` | `short[]` | |
| 48 | `CacheableInt32Array` | `int[]` | VL 邊界（252 / 253 / 65536）unit test 集中寫在這檔，其他 array 共用 ReadArrayLen/WriteArrayLen 不重複 |
| 49 | `CacheableInt64Array` | `long[]` | |
| 50 | `CacheableFloatArray` | `float[]` | IEEE-754 BE，NaN / ±Infinity bit-pattern 保留 |
| 51 | `CacheableDoubleArray` | `double[]` | |
| 64 | `CacheableStringArray` | `string[]` | **唯一**收 `SerializationRegistry` ctor 注入；每元素重入 `WriteObject` 走完整 DSCode dispatch（per-element 42 / 87 / 88 / 89 / 41 都可能）；`null` 元素走 NullObj=41 由 registry 一層處理；`new this(this)` 安全（converter 只存 reference、Write/Read 才使用，那時 registry 已完整 populated） |

**Tier B-2 — 集合（進行中）**

- ✅ `CacheableObjectArray(52)` — commit `0671ae1`。`object[]` ↔ 寫死 `"java.lang.Object"` Java class header + per-element re-entry 透過 registry。
- ✅ `CacheableArrayList(65)` — `List<T>` / `IList<T>` 端到端。架構新增**兩個機制**支撐這個 tier 的後續所有集合：
  - **`TypedResultAdapter`**（Scoped DI；[Protocol/Serialization/TypedResultAdapter.cs](src/Geode.Client/Protocol/Serialization/TypedResultAdapter.cs)）— Java wire 不帶 container element type，decode 永遠回 canonical `List<object?>`；adapter 在 `RegionView` 邊界把 `object?` 重塑成宣告 `TValue`（`IList<int>` / `IList<IList<string>>` / `int[]` 都通），遞迴下降處理 nested generics。Two-pass cost MVP 可接受；profiling 顯示問題才把 hint 下推到 converter（API 不會破壞）
  - **`SerializationRegistry` open-generic write fallback**（[SerializationRegistry.cs:140-149](src/Geode.Client/Protocol/Serialization/SerializationRegistry.cs)）— `_byType[runtimeType]` miss 且 `runtimeType.IsGenericType` 時二次查 `GetGenericTypeDefinition()`；單字典雙探，不增加索引。`ListDataConverter.ManagedType = typeof(List<>)` 一個 instance 通吃所有 `List<T>` 閉式具現
  - 涉檔：上述兩支 + [ListDataConverter.cs](src/Geode.Client/Protocol/Serialization/ListDataConverter.cs) / [RegionView.cs](src/Geode.Client/Services/RegionView.cs)（adapter 注入）/ [Cache.cs](src/Geode.Client/Services/Cache.cs)（primary ctor 多收 adapter）/ [GeodeClientExtensions.cs](src/Geode.Client/GeodeClientExtensions.cs)（Scoped DI 註冊）
  - 測試：37 個新 unit（TypedResultAdapter 23 / ListDataConverter 9 / SerializationRegistry open-generic dispatch 5）+ 7 個新 integration（含 1 個 B-route 驗 server-side `java.util.ArrayList`）。422 unit + 既有整合測試全綠
  - **gfsh quirk**（記到 memory）：`gfsh get` 印 ArrayList 用 `[1,2,3]`（無空格），不是標準 Java `[1, 2, 3]`；B-route regex 要用無空格版本
- [ ] `CacheableHashSet(66)` — `HashSet<T>` / `ISet<T>`；同 ArrayList 套路（adapter 加 `ISet<>` branch、Set converter `ManagedType=typeof(HashSet<>)`）
- [ ] `CacheableHashMap(67)` — `Dictionary<K,V>` / `IDictionary<K,V>`；adapter 加 `IDictionary<,>` branch + key/value 雙遞迴；converter `ManagedType=typeof(Dictionary<,>)`
- [ ] `CacheableLinkedList(10)` / `CacheableVector(71)` / `CacheableStack(74)` / `CacheableLinkedHashSet(73)` — 等真有需求再補

**Tier C — 不做或 Phase 2+：**
`NullObj(41)` 已內聯；`CacheableNullString(69)` 走 41 即可；`PdxType/PDX/PDX_ENUM` Phase 2；`CacheableUserData*` Phase 2；`Properties(11)` Phase 3 auth；`JavaSerializable(44)`/`DataSerializable(45)`/`Class(43)`/`CacheableFileName(63)`/`CacheableTimeUnit(68)` 罕用，skip；`FixedID*(1–4)` 是 wire layer 內部碼，不放 `SerializationRegistry`。

---

### 1.3.a — Clear + Invalidate（非分片）✅

**完工狀態**：323 unit tests（先前 292 + 新增 31）+ 22 integration tests（先前 17 + 新增 5）全綠對 `apachegeode/geode` 真機。

- [x] `IRegion.ClearAsync(CancellationToken)` / `IRegion.InvalidateAsync(object, CancellationToken)` + typed `IRegion<TKey,TValue>.InvalidateAsync(TKey, CancellationToken)`（無 typed `ClearAsync` overload — 無 K/V 參數）
- [x] `RegionInternal` 加 2 個 abstract；`RegionView` typed forward + 顯式 `IRegion.InvalidateAsync` 實作
- [x] `ClearRegion(36)` — 2 parts（regionName / eventId）或 3 parts（含 callback）；對齊 cppcache `TcrMessageClearRegion` (`TcrMessage.cpp:1644-1682`)；reply `Reply(6)` / `ClearRegionDataError(37)` / `Exception(2)` / 其他 → throw；沒有 chunked
  - [Protocol/TcrMessageBuilder.ClearRegion.cs](src/Geode.Client/Protocol/TcrMessageBuilder.ClearRegion.cs)
  - `millisecondsResponseTimeout` part **不實作** — cppcache `ThinClientRegion::clear` (`ThinClientRegion.cpp:777`) 寫死傳 `-1`，正常路徑從不發
  - `localClearNoThrow` + `invokeCacheListenerForRegionEvent(AFTER_REGION_CLEAR)` 略過（Phase 2+ caching-enabled 才需要）
- [x] `Invalidate(83)` — 3 parts（regionName / key / eventId）或 4 parts（含 callback）；對齊 cppcache `TcrMessageInvalidate` (`TcrMessage.cpp:1896-1932`)；reply `Reply(6)` / `Exception(2)` / `InvalidateError(84)` / 其他 → throw；versionTag 先丟（同 `RemoveAsync`）
  - [Protocol/TcrMessageBuilder.Invalidate.cs](src/Geode.Client/Protocol/TcrMessageBuilder.Invalidate.cs)
  - 比 Destroy 少 `expectedOldValue` / `Operation` 兩個 NullObj part（Invalidate 沒有 conditional overload 共用 ctor）
- [x] `ThinClientRegion.ClearAsync` / `InvalidateAsync` 端到端 — 日誌對齊 cppcache `LOGFINE` / `LOGERROR` 嚴重度
- [x] Unit tests — `TcrMessageBuilderClearRegionTests`（15 cases）+ `TcrMessageBuilderInvalidateTests`（16 cases）
- [x] [RegionInvalidateClearIntegrationTests](tests/Geode.Client.IntegrationTests/RegionInvalidateClearIntegrationTests.cs) — 5 cases（Invalidate keeps key clears value / missing-key invalidate OK / Put after Invalidate restores / Clear removes-all keeps-region / Clear on empty region OK）

**不暴露**：`InvalidateRegion(55)` 是 server→client only，要 region-wide 就 `ClearAsync`

### 1.3.b — Chunked-reply 基建 + RemoveAll ✅

**完工狀態**：5/5 RemoveAll integration tests 通過對 `apachegeode/geode` 真機。Chunked-reply 解碼整條 wire 跑通（含 versioned region 的 `VersionTag.FromData` 路徑）。

#### Wire 請求 + 入口

- [x] `RemoveAll(109)` — 5+keys.Count parts（region / eventId / flags=0 / callback-or-NullObj / keyCount / N keys）；對齊 cppcache `TcrMessageRemoveAll` (`TcrMessage.cpp:2424-2468`)
  - [Protocol/TcrMessageBuilder.RemoveAll.cs](src/Geode.Client/Protocol/TcrMessageBuilder.RemoveAll.cs)
- [x] `EventIdGenerator.NextRange(int count)` — Interlocked.Add 一次保留 N 個連續 seq id（cppcache `writeEventIdPart(keys.size()-1)` 對應）
- [x] `IRegion.RemoveAllAsync(IReadOnlyCollection<object>, ct)` + `IRegion<TKey,TValue>.RemoveAllAsync(IReadOnlyCollection<TKey>, ct)` + `RegionView` typed forward（reference TKey 走 covariance、value TKey box 進 `object[]`）
- [x] `ThinClientRegion.RemoveAllAsync` body — build → `EventIdGenerator.NextRange(N)` → dispatch → REPLY/RESPONSE/EXCEPTION switch

#### DM / 連線層 chunked 路徑

- [x] `ThinClientBaseDM.SendSyncRequestAsync(TcrMessage, TcrChunkedResult, ...)` abstract overload
- [x] `ThinClientPoolDM.SendSyncRequestAsync(req, chunkedResult, ...)` — SelectEndpoint → AddEP → forward
- [x] `ThinClientPoolDM.SendRequestToEndpointAsync` chunked overload — borrow conn → 呼 `TcrConnection.SendRequestAsync(req, chunkedResult, ct)` → put-back / disconnect-on-error，整體跟非 chunked overload 形狀對齊
- [x] `TcrConnection.SendRequestAsync(req, TcrChunkedResult, ct)` overload — **inline chunked-reply 迴圈**（cppcache `readMessageChunked` 對應）：17-byte 首 frame header + 5-byte 後續 chunk header + last-chunk bit
- [x] `TcrConnection.Touch()` 空殼 + `PutInQueueAsync` 呼叫（Phase 1.5 `cleanStaleConnections` 用 `_lastAccessed` 真填）

**關鍵設計校正**：cppcache `m_pendingReplies` / 背景 reader 那層**我們不需要**。cppcache chunked 路徑是 **inline** 同步讀（`readMessageChunked` 在發送 thread 上接著跑），一條 connection 一次只服務一個 request。Audit 前期誤判要做 `_pendingReplies` 表跟背景 reader，看 cppcache 真碼後刪掉。

#### Chunked-result handler 階層

- [x] `TcrChunkedResult` abstract base（[Protocol/TcrChunkedResult.cs](src/Geode.Client/Protocol/TcrChunkedResult.cs)）— `HandleChunk(payload, isLastChunk)` + `Reset()`；cppcache 的 `finalize` / `binary_semaphore` / `m_ex` / `m_dsmemId` 槽位全部砍掉（Task/await + exception 自然冒泡 + Phase 4 才需要 dsmemId）
- [x] `ChunkedRemoveAllResponse` ([Services/ChunkedRemoveAllResponse.cs](src/Geode.Client/Services/ChunkedRemoveAllResponse.cs)) — `Reset` 對齊 cppcache 2 步（null+size guard → clear versionTags）；`HandleChunk` 5 步：
  - Step 1：wrap payload 進 `BigEndianBinaryReader`（via `ActivatorUtilities`）
  - Step 2：`TcrMessageHelper.ReadChunkPartHeader` 分類 chunk
  - Step 3a：`NullObject` → return（空 reply）
  - Step 3b：`Object` → `new VersionedCacheableObjectPartList` + `FromData` + `list?.AddAll`
  - Step 3c：`Bytes` → 讀 2 bytes（single-hop metadata，Phase 4 真用）
  - fallthrough：`Exception` / unknown → throw `GeodeException`
- [x] `TcrMessageHelper.ReadChunkPartHeader` — 9 步完整 impl（partLen + isObj → NullObject / Exception 早出；DSCode 分支 JavaSerializable / NullObj / FixedIDByte+compId / 不符 → throw）
- [x] `ChunkObjectType` enum（`NullObject` / `Object` / `Exception` / `Bytes`）

#### VersionedObjectPartList 解碼器（真實作）

- [x] `CacheableObjectPartList` base（cppcache 對齊；primary ctor 收 `RegionInternal region`；9 個 protected 欄位 mirror cppcache `m_*`）
- [x] `VersionedCacheableObjectPartList` — primary ctor `(IServiceProvider, SerializationRegistry, ILogger, RegionInternal)`；
  - 7 個 wire 欄位 + 4 個 FLAG_* 常數 + `VersionTags` accessor + `Size` 屬性（cppcache `size()` 對應）
  - `FromData` 7 步真實作（在 `lock(_responseLock)` 內）：flags byte parse / init Values / 空訊息 LogDebug / keys section（`_hasKeys` 真讀 keys → tempKeys/ResultKeys/localKeys） / objects section（`hasObjects` → `ReadObjectPart` 進 _byteArray+Values） / version tags section（`_hasTags` switch on 4 FLAG_*） / putLocal merge（Phase 4+ NIE）
  - `AddAll(other)` 真實作（cppcache `addAll` 3 步：merge keys / OR-in regionIsVersioned / merge versionTags）
  - `ReadObjectPart` 真實作（3 分支：exception=2 → wrap `GeodeException` 進 `Exceptions` / `_serializeValues=true` → raw bytes / 一般 → `serializationRegistry.ReadObject`）
- [x] `BigEndianBinaryReader.ReadUnsignedVL` 真實作（Java VL unsigned u64，1-9 bytes、9-byte cap throw `InvalidDataException`）
- [x] `BigEndianBinaryReader.AdvanceCursor(int)` 真實作 / `ReadString` 暫 NIE（exception part 才呼到）

#### VersionTag + DiskVersionTag

- [x] `VersionTag` — primary ctor `(IServiceProvider, ILogger, MemberListForVersionStamp?)`；7 個欄位（`_bits` / `_entryVersion` / `_regionVersionHighBytes` / `_regionVersionLowBytes` / `_internalMemId` / `_previousMemId` / `_timeStamp`）+ 5 個 `HAS_*`/`VERSION_TWO_BYTES`/`DUPLICATE_MEMBER_IDS` 常數 + 3 個 `BITS_*` 常數
  - `FromData` 8 步真實作（flags / bits / skip distributedSystemId / entryVersion 16-or-32 / regionVersionHighBytes optional / regionVersionLowBytes / timeStamp VL / virtual `ReadMembers` 派發）
  - `ReadMembers` 2 步真實作（`HAS_MEMBER_ID` → `ClientProxyMembershipID.ReadEssentialData` + `MemberListForVersionStamp.Add` → `_internalMemId`；`HAS_PREVIOUS_MEMBER_ID` 含 `DUPLICATE_MEMBER_IDS` 短路）
  - `ReplaceNullMemberId(memId)` 真實作（4 行 if-設值）
- [x] `DiskVersionTag` (`internal sealed : VersionTag`) — `ReadMembers` override NIE（persistent region 才碰到 DiskStoreId 解碼，Phase 4+）
- [x] `ClientProxyMembershipID` — primary ctor 收 `SerializationRegistry`（DI 注入）；`ReadEssentialData` 真實作（cppcache 7-field wire format：array length + hostAddr bytes + hostPort + skip flag + vmKind + uniqueTag/vmViewIdStr（loner 分支） + dsName）
- [x] `MemberListForVersionStamp` — `Add` 真實作（簡化版：monotonic id 不做 hashKey dedup，Phase 4 補）；`GetDsMember` 真實作（dict lookup + lock）
- [x] `DSFid` enum（25 個 entry，含 `VersionedObjectPartList = 7` / `DiskVersionTag = 2131` 等，跟 cppcache 1:1）

#### 命名 / 型別注入慣例

- [x] CLAUDE.md 第 9 條原則：**cppcache wire 鏡像常數用 `SCREAMING_SNAKE_CASE`**（`FLAG_NULL_TAG` / `HAS_MEMBER_ID`）；自製 C# 常數 PascalCase（`MetaTransactionId` / `ThreadId`）。`.editorconfig` 不強制
- [x] **Internal 類別注入「最具體必要型別」而非介面**：`ChunkedRemoveAllResponse` 收 `ThinClientRegion`、`VersionedCacheableObjectPartList` / `CacheableObjectPartList` 收 `RegionInternal`——避免 future downcast 風險
- [x] **`ActivatorUtilities.CreateInstance` 廣泛採用**：`ChunkedRemoveAllResponse` / `VersionedCacheableObjectPartList` / `VersionTag` / `DiskVersionTag` / `ClientProxyMembershipID` / `BigEndianBinaryReader` 都走 ActivatorUtilities，DI 依賴自動注入

#### 測試

- [x] [RegionRemoveAllIntegrationTests](tests/Geode.Client.IntegrationTests/RegionRemoveAllIntegrationTests.cs) — 5 cases（4-key batch / mixed present+missing / empty arg / null arg / single-key N=1 邊界）全綠對 `apachegeode/geode` 真機，15 秒
- [ ] Unit tests — `TcrMessageBuilderRemoveAllTests`（頭尾 shape / 5+N parts / per-part payload / arg validation / encode round-trip）尚未寫；整合測試已覆蓋 happy path

#### Deferred / 留待後續

- **NIE 仍存在但 RemoveAll 不踩**：`DiskVersionTag.ReadMembers`（persistent region，Phase 4+）/ `BigEndianBinaryReader.ReadString`（exception chunk，Phase 1.3.c GetAll 才可能）/ Step 7 `putLocal` merge（`AddToLocalCache`，Phase 4+ client-side caching）
- **欄位仍 placeholder**：`_endpointMemId` / `_msg`（pragma CS0649 包住）—— Phase 3 auth / Phase 4 single-hop 才寫入
- `MemberListForVersionStamp.Add` 的 hashKey dedup 跳過——需要 `ClientProxyMembershipID.HashKey`，Phase 4 補
- **架構決策已收進 memory 或 CLAUDE.md**：
  - constants naming convention（CLAUDE.md #9）
  - internal class 注入最具體型別（待 memory）

### 1.3.c — PutAll + GetAll70

- [ ] `PutAll(56)` — 5+map.size*2 parts；同 1.3.b chunked 路徑
- [ ] `IRegion.PutAllAsync(IReadOnlyDictionary<TKey,TValue>, CancellationToken)`
- [ ] `GetAll70(100)` — 砍 tracker map / exception map，只回 `IReadOnlyDictionary<TKey, TValue?>`（exception 路徑等真的有需求再補）
- [ ] `IRegion.GetAllAsync(IReadOnlyCollection<TKey>, CancellationToken)`

### Phase 1.3 共用決策

- bulk ops 進用 `IReadOnlyDictionary` / `IReadOnlyCollection`、出用新 `Dictionary` / `IReadOnlyDictionary`（.NET 慣例 + 不洩漏內部 mutable state）
- versionTag 全部忽略（讀完丟），同 Phase 1.2 `RemoveAsync`；Phase 4 client-side cache / delta 才回填
- **Key 型別約束**：`IRegion<TKey, TValue>` 加 `where TKey : IEquatable<TKey>`（cppcache `CacheableKey` 強制 `operator==` + `hashcode()` 的 .NET 等效）
  - 編譯期擋住 `byte[]`（`Array` 不實作 `IEquatable<T>`）、`List<>` / `Dictionary<>` / `HashSet<>` 等集合、未實作 `IEquatable<T>` 的 user POCO
  - PDX user class（Phase 2）必須實作 `IEquatable<T>`，強迫使用者面對 Java server 端 `equals` / `hashCode` 語意問題
  - **沒對應 converter 的型別只能 runtime 擋**：`IRegion<MyType, ...>` 編譯過、但 `SerializationRegistry.WriteObject` 找不到 `_byType[typeof(MyType)]` 時 throw `NotSupportedException`（既有行為，不用動）
  - 非泛型 `IRegion` 不加約束（untyped `GetRegion` 回它，cast 到泛型版時編譯期擋）

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
