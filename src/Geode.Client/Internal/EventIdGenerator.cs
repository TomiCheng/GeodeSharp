namespace Geode.Client.Internal;

/// <summary>
/// Per-cache generator for the <c>(threadId, sequenceId)</c> pair the
/// server uses to dedup write events. Mirrors cppcache
/// <c>EventIdTSS</c> (<c>cppcache/src/EventId.cpp:42-77</c>) — the
/// <c>thread_local</c> singleton that hands every <c>writeEventIdPart</c>
/// a fresh id pair.
/// </summary>
/// <remarks>
/// <para>
/// <b>Wire role.</b> Each <c>Put</c> / <c>PutAll</c> / <c>Destroy</c> /
/// <c>Invalidate</c> request carries an <c>EventId</c> part containing
/// these two i64 values. The server dedups by
/// <c>(clientId, threadId, sequenceId)</c>: replaying the same triple
/// is silently dropped. <c>clientId</c> is process-static (see
/// <see cref="ClientProxyMembershipIdBuilder"/>), so uniqueness here
/// boils down to ensuring no two write events in the same cache
/// observe the same <c>(threadId, sequenceId)</c>.
/// </para>
/// <para>
/// <b>Why we don't mirror cppcache exactly.</b> cppcache assigns a
/// monotonic global <c>threadId</c> to each OS thread the first time
/// it touches the TSS, then increments a thread-local
/// <c>sequenceId</c>. That design assumes thread affinity for the
/// lifetime of a logical operation — which C# async breaks. Awaiting
/// can resume on a different pool thread, so a
/// <c>ThreadLocal&lt;long&gt;</c> here would silently collide.
/// </para>
/// <para>
/// Simpler scheme that preserves the contract: one fixed
/// <see cref="ThreadId"/> (= 1) per cache scope plus a single
/// <see cref="Interlocked.Increment(ref long)"/> on
/// <c>_sequenceId</c>. Each call to <see cref="Next"/> hands back
/// <c>(1, ++_sequenceId)</c>. Globally unique inside this cache, no
/// thread-affinity assumption, no awaitable hazard. Same wire effect
/// as cppcache (server still dedups on the triple) — we just collapse
/// the cppcache's two-level (threadId × seq) namespace into a single
/// long counter.
/// </para>
/// <para>
/// <b>Scope: Scoped (per-cache).</b> Different
/// <see cref="Services.Cache"/> instances do not share a counter —
/// each cache has its own random <c>uniqueTag</c> in
/// <see cref="ClientProxyMembershipIdBuilder"/>, so the
/// <c>clientId</c> the server sees differs per cache and the
/// <c>(clientId, threadId, seq)</c> dedup triple is naturally unique
/// across caches even when both reset seq from 0. Mirrors cppcache:
/// <c>EventIdTSS</c> lives inside <c>CacheImpl</c> (no static
/// state), and each <c>CacheImpl</c> also owns its own
/// <c>ClientProxyMembershipIDFactory.randString_</c>.
/// </para>
/// </remarks>
internal sealed class EventIdGenerator
{
    /// <summary>
    /// The fixed thread component every event in this cache reports.
    /// cppcache uses a per-OS-thread value here; we collapse to a
    /// single constant because async work can't carry thread affinity
    /// across awaits (see class remarks).
    /// </summary>
    public const long ThreadId = 1L;

    private long _sequenceId;

    /// <summary>
    /// Allocate the next <c>(threadId, sequenceId)</c> pair. Thread-safe
    /// — concurrent callers always receive distinct sequence ids.
    /// </summary>
    public (long ThreadId, long SequenceId) Next()
    {
        var seq = Interlocked.Increment(ref _sequenceId);
        return (ThreadId, seq);
    }
}
