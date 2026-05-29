using System;
using System.Collections.Generic;
using System.Text;
using Geode.Client.Protocol;

namespace Geode.Client.Internal;

partial class LocalRegion
{
    class PutActions(LocalRegion localRegion,
        object key, object value, object? callbackArgument, int updateCount, CacheEventFlags eventFlags)
        : IRegionAction
    {
        /// <summary>
        /// cppcache <c>PutActions::m_txState</c>
        /// (<c>LocalRegion.cpp:1102</c>) — ctor 一次性 capture
        /// 當下 thread / async-flow 的 TX,後續 pipeline / Tx 變體
        /// 都讀同一份。Phase 1.x <see cref="GetTXState"/> 永遠回
        /// <see langword="null"/>,Phase 4+ transactions 落地才會有值。
        /// </summary>
        public TXState? TxState { get; } = localRegion.GetTXState();

        public EntryEventType BeforeEventType => throw new NotImplementedException();

        public EntryEventType AfterEventType => throw new NotImplementedException();

        public bool AddIfAbsent => throw new NotImplementedException();

        public bool FailIfPresent => throw new NotImplementedException();

        public string Name => throw new NotImplementedException();

        /// <summary>
        /// cppcache pipeline 寫 / 讀的 inout 欄位 — writer hook
        /// (<see cref="GetCallbackOldValue"/>) 寫入,後續 listener
        /// AFTER hook + localUpdate 讀。Phase 1.x 沒人 set 永遠 null。
        /// </summary>
        public object? OldValue { get; set; }
        public object Key => throw new NotImplementedException();
        public object Value => throw new NotImplementedException();
        public object? CallbackArgument => throw new NotImplementedException();
        public CacheEventFlags EventFlags => throw new NotImplementedException();
        public int UpdateCount { get; set; } = updateCount;
        public VersionTag? VersionTag { get; set; }
        public MapEntry? Entry => throw new NotImplementedException();

        public void CheckArgs() => throw new NotImplementedException();
        public void GetCallbackOldValue() => throw new NotImplementedException();
        public Task LocalUpdateAsync(int updateCount, bool remoteOpDone, CancellationToken ct) => throw new NotImplementedException();
        public void LogCacheWriterFailure() => throw new NotImplementedException();
        public Task RemoteUpdateAsync(CancellationToken ct) => throw new NotImplementedException();
    }
}
