namespace Geode.Client.Internal;

/// <summary>
/// Async-safe reader-writer lock. <b>Phase 1.x degraded impl</b> —
/// reader / writer 走同一個 <see cref="SemaphoreSlim"/>(1, 1),
/// reader 並發退化成排他,跟 plain mutex 等效。API shape 對齊真正
/// 的 RW lock,Phase 5 / 2+ 把實作換掉時 call sites 完全不動。
/// </summary>
/// <remarks>
/// <para>
/// 之所以走「排他鎖偽裝成 RW lock」這條路:
/// </para>
/// <list type="bullet">
///   <item>BCL only(沒 Nito.AsyncEx 相依,符合 CLAUDE.md「零 runtime
///         相依」規則)。</item>
///   <item>跨 <c>await</c> 持鎖安全(<see cref="ReaderWriterLockSlim"/>
///         做不到 — 它綁 thread ID)。</item>
///   <item>Phase 1.x proxy-only,沒 reader 並發場景,排他鎖跟真 RW
///         鎖效能無差。</item>
/// </list>
/// <para>
/// Phase 2+ caching-enabled 後,reader 路徑 (Get / ContainsKey /
/// CheckDestroyPending) 變多,排他鎖就會變吞吐瓶頸 — 屆時把實作
/// 升級成真正的 reader-counter + writer-semaphore 機制(或吞 Nito
/// 相依),caller side 因為 API shape 一致不用動。
/// </para>
/// </remarks>
internal sealed class AsyncReaderWriterLock : IDisposable
{
    private readonly SemaphoreSlim _sem = new(1, 1);

    /// <summary>取 reader 鎖。Phase 1.x 等同 writer 鎖(排他)。</summary>
    public Task EnterReadLockAsync(CancellationToken ct = default) => _sem.WaitAsync(ct);

    /// <summary>釋放 reader 鎖。</summary>
    public void ExitReadLock() => _sem.Release();

    /// <summary>取 writer 鎖(排他)。</summary>
    public Task EnterWriteLockAsync(CancellationToken ct = default) => _sem.WaitAsync(ct);

    /// <summary>釋放 writer 鎖。</summary>
    public void ExitWriteLock() => _sem.Release();

    public void Dispose() => _sem.Dispose();
}
