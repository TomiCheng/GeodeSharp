namespace Geode.Client.Internal;

/// <summary>
/// Expiration bookkeeping for one <see cref="MapEntry"/>: monotonic
/// last-accessed / last-modified timestamps plus the scheduled
/// expiry-task handle. Mirrors cppcache <c>ExpEntryProperties</c>
/// (<c>cppcache/src/ExpEntryProperties.hpp:38-90</c>).
/// </summary>
/// <remarks>
/// <para>
/// cppcache stores <c>std::atomic&lt;rep&gt;</c> tick counts of
/// <c>steady_clock</c>; the C# port stores <see cref="long"/>
/// <see cref="System.Diagnostics.Stopwatch.GetTimestamp"/> ticks with
/// <see cref="System.Threading.Interlocked"/> access. <b>Monotonic, not
/// wall-clock</b> — expiry compares elapsed time, so a clock adjustment
/// must not move an entry's age (matches cppcache's <c>steady_clock</c>).
/// </para>
/// <para>
/// The <c>ExpiryTaskManager</c> integration
/// (<see cref="TaskId"/> / <see cref="TaskScheduled"/> /
/// <see cref="CancelTask"/>) lands when the expiry scheduler is built;
/// until then those members throw <see cref="NotImplementedException"/>.
/// </para>
/// </remarks>
internal class ExpEntryProperties
{
    /// <summary>Last-accessed timestamp, Stopwatch ticks; <c>0</c> = never.</summary>
    private long _lastAccessed;

    /// <summary>Last-modified timestamp, Stopwatch ticks; <c>0</c> = never.</summary>
    private long _lastModified;

    /// <summary>
    /// Monotonic last-accessed timestamp (Stopwatch ticks). Mirrors cppcache
    /// <c>last_accessed()</c> / <c>last_accessed(tp)</c>
    /// (<c>ExpEntryProperties.hpp:44-46,52-54</c>) — the getter/setter
    /// overloads collapse to one property; atomic via
    /// <see cref="System.Threading.Interlocked"/>.
    /// </summary>
    public long LastAccessed
    {
        get => Interlocked.Read(ref _lastAccessed);
        set => Interlocked.Exchange(ref _lastAccessed, value);
    }

    /// <summary>
    /// Monotonic last-modified timestamp (Stopwatch ticks). Mirrors cppcache
    /// <c>last_modified()</c> / <c>last_modified(tp)</c>
    /// (<c>ExpEntryProperties.hpp:48-50,56-58</c>).
    /// </summary>
    public long LastModified
    {
        get => Interlocked.Read(ref _lastModified);
        set => Interlocked.Exchange(ref _lastModified, value);
    }

    /// <summary>
    /// Whether an expiry task is scheduled for this entry. Mirrors cppcache
    /// <c>task_scheduled()</c> (<c>ExpEntryProperties.hpp:62</c>). Pending
    /// the <c>ExpiryTaskManager</c>.
    /// </summary>
    public virtual bool TaskScheduled => throw new NotImplementedException(
        "ExpEntryProperties.TaskScheduled: pending ExpiryTaskManager.");

    /// <summary>
    /// Record the scheduled expiry task's id. Mirrors cppcache
    /// <c>task_id(id)</c> (<c>ExpEntryProperties.hpp:60</c>). Pending the
    /// <c>ExpiryTaskManager</c>.
    /// </summary>
    public virtual void SetTaskId(long taskId) => throw new NotImplementedException(
        "ExpEntryProperties.SetTaskId: pending ExpiryTaskManager.");

    /// <summary>
    /// Cancel the scheduled expiry task. Mirrors cppcache <c>cancel_task()</c>
    /// (<c>ExpEntryProperties.hpp:64</c>). Pending the
    /// <c>ExpiryTaskManager</c>.
    /// </summary>
    public virtual void CancelTask() => throw new NotImplementedException(
        "ExpEntryProperties.CancelTask: pending ExpiryTaskManager.");
}
