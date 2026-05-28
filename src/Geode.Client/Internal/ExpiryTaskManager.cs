namespace Geode.Client.Internal;

/// <summary>
/// Cache-scoped scheduler for region / entry TTL, idle timeouts and
/// tombstone GC. Mirrors cppcache <c>ExpiryTaskManager</c>
/// (<c>cppcache/src/ExpiryTaskManager.hpp:44</c>). Skeleton only —
/// members land with the expiration phase (Phase 2+); the cppcache
/// <c>boost::asio::io_context</c> + runner thread is likely collapsed
/// onto <see cref="System.Threading.PeriodicTimer"/> /
/// <see cref="System.Threading.Tasks.Task.Delay(TimeSpan, System.Threading.CancellationToken)"/>
/// when the body lands.
/// </summary>
internal sealed class ExpiryTaskManager
{
}
