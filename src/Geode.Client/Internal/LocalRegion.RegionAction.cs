using System;
using System.Collections.Generic;
using System.Text;
using Geode.Client.Protocol;

namespace Geode.Client.Internal;

partial class LocalRegion
{
    interface IRegionAction
    {
        // strategy methods (cppcache TAction 的隱式契約) — interface 顯式宣告
        void CheckArgs();
        Task RemoteUpdateAsync(CancellationToken ct);
        // 非成功路徑(race-loser / concurrent-mod / invalid-delta 等)
        // 透過 GfErrTypeException 攜帶 GfErrType code 拋出,pipeline
        // catch 後 switch ex.Code 分流。維持「*Async 走 exception」設計。
        Task LocalUpdateAsync(int updateCount, bool remoteOpDone, CancellationToken ct);
        void GetCallbackOldValue();
        void LogCacheWriterFailure();

        // captured state (cppcache TAction 的 instance field)
        TXState? TxState { get; }

        // constants
        EntryEventType BeforeEventType { get; }
        EntryEventType AfterEventType { get; }
        bool AddIfAbsent { get; }
        bool FailIfPresent { get; }
        string Name { get; }
        object? OldValue { get; set; }
        object Key { get; }
        object? Value { get; }
        object? CallbackArgument { get; }
        CacheEventFlags EventFlags { get; }
        int UpdateCount { get; set; }
        VersionTag? VersionTag { get; set; }
        MapEntry? Entry { get; set; }
    }
}
