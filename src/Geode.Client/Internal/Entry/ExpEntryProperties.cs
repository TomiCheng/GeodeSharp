namespace Geode.Client.Internal.Entry;

/// <summary>
/// Per-entry expiration bookkeeping (last-accessed / last-modified timestamps
/// + expiry-task handle). Mirrors cppcache <c>ExpEntryProperties</c>
/// (<c>cppcache/src/ExpEntryProperties.hpp:38</c>).
/// </summary>
internal class ExpEntryProperties
{
    // cppcache last_accessed_ / last_modified_ are std::atomic<duration::rep>
    //   (tick counts); long + Interlocked here.
    private long _lastAccessed;
    private long _lastModified;

    /// <summary>cppcache <c>last_accessed()</c> get/set — tick count.</summary>
    public long LastAccessed
    {
        get => Interlocked.Read(ref _lastAccessed);
        set => Interlocked.Exchange(ref _lastAccessed, value);
    }

    /// <summary>cppcache <c>last_modified()</c> get/set — tick count.</summary>
    public long LastModified
    {
        get => Interlocked.Read(ref _lastModified);
        set => Interlocked.Exchange(ref _lastModified, value);
    }

    /// <summary>
    /// Whether an expiry task is scheduled. cppcache <c>task_scheduled()</c>
    /// (<c>task_id_ != ExpiryTask::invalid()</c>). Pending the
    /// <c>ExpiryTaskManager</c> / task-id sentinel.
    /// </summary>
    public bool TaskScheduled => throw new NotImplementedException(
        "ExpEntryProperties.TaskScheduled: pending ExpiryTaskManager.");

    /// <summary>Set the expiry-task id. cppcache <c>task_id(id)</c>. Pending the scheduler.</summary>
    public void SetTaskId(long taskId) => throw new NotImplementedException(
        "ExpEntryProperties.SetTaskId: pending ExpiryTaskManager.");

    /// <summary>Cancel the scheduled expiry task. cppcache <c>cancel_task()</c>. Pending the scheduler.</summary>
    public void CancelTask() => throw new NotImplementedException(
        "ExpEntryProperties.CancelTask: pending ExpiryTaskManager.");
}
