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

## Phase 1.3 — Bulk + management ops

### 1.3.0 — `IDataConverter` 內建型別擴充 ✅

Phase 1.2 只實作 `Int32` + `Boolean` 兩個 converter；bulk ops 端到端整合測試要更有代表性的 K/V 型別。先把 MVP scalar / string / bytes 一次補齊，後面 1.3.a–1.3.c 都吃這個前置。

**完工狀態**：
- 11 個 Tier A converter src + unit tests + integration tests 全綠（292 unit + 17 integration）
- `IDataConverter` API 改造完成（`DsCodes[]` / `GetDsCode(value)` / `Write(w, v, dsCode)` / `Read(r, dsCode)`），cppcache `Serializable::getDsCode()` 對齊
- `IRegion<TKey, TValue>` constraint `where TKey : IEquatable<TKey>`（編譯期擋集合 / `byte[]` / 無 IEquatable POCO）
- 順手修了 `BigEndianBinaryReader.ReadArrayLen` signed/unsigned bug（phase 1.1 留下來的潛在問題，length 128..252 被誤判負數）

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

**Tier B — 視 demo / 測試需要再加**（不在 1.3.0 範圍）：
- `CacheableArrayList(65)` / `CacheableHashSet(66)` / `CacheableHashMap(67)` / `CacheableObjectArray(52)`
- primitive arrays（47–51, 26, 27, 64）
- 一旦觸發 Tier B，要實作「encode 端介面分派」（`IList` / `IDictionary` / `ISet` 偵測 + 泛型 element 遞迴 `WriteObject`），cppcache 走 RTTI dynamic_cast 對齊。

**Tier C — 不做或 Phase 2+：**
`NullObj(41)` 已內聯；`CacheableNullString(69)` 走 41 即可；`PdxType/PDX/PDX_ENUM` Phase 2；`CacheableUserData*` Phase 2；`Properties(11)` Phase 3 auth；`JavaSerializable(44)`/`DataSerializable(45)`/`Class(43)`/`CacheableFileName(63)`/`CacheableTimeUnit(68)` 罕用，skip；`FixedID*(1–4)` 是 wire layer 內部碼，不放 `SerializationRegistry`。

---

### 1.3.a — Clear + Invalidate（非分片）

- [ ] `ClearRegion(36)` — 3 parts（regionName / eventId / [callback]）；reply `Reply(6)` 或 `ClearRegionDataError(37)` 或 `Exception(2)`；沒有 chunked
- [ ] `Invalidate(83)` — 3 parts（regionName / key / eventId / [callback]）；reply `Reply(6)` 或 `InvalidateError(84)` 或 `Exception(2)`；versionTag 先丟（同 `RemoveAsync`）
- [ ] `IRegion.ClearAsync(CancellationToken)` / `IRegion.InvalidateAsync(TKey, CancellationToken)`
- [ ] `InvalidateRegion(55)` 是 server→client only，**不暴露** `InvalidateRegionAsync`（要 region-wide 就 `ClearAsync`）

### 1.3.b — Chunked-reply 基建 + RemoveAll

- [ ] `TcrConnection` chunked reader（讀到 `lastChunkBit` 才結束；對齊 cppcache `TcrMessage::handleByteArrayResponse`）
- [ ] `ChunkedResponseHandler` 抽象（對齊 cppcache `TcrChunkedResult`）
- [ ] `VersionedCacheableObjectPartList` 解碼器（thin-client 路徑：忽略 versionTags、認 `NULL_OBJECT` / `byteArray[i]==3` miss）
- [ ] `_pendingReplies` 改成「send 時註冊 handler」，reply reader 不再反推 chunked / 非 chunked
- [ ] `RemoveAll(109)` — 5+keys.size parts
- [ ] `IRegion.RemoveAllAsync(IReadOnlyCollection<TKey>, CancellationToken)`

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
