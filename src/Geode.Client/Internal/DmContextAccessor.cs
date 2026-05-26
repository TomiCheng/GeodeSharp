namespace Geode.Client.Internal;

/// <summary>
/// Ambient access to the <see cref="ThinClientBaseDM"/> for the current
/// region-op (Put / Get / Query / etc.) — same shape as ASP.NET Core's
/// <c>HttpContextAccessor</c>. Lets long-lived services
/// (<c>SerializationRegistry</c>, <c>PdxTypeRegistry</c>, ...) reach the
/// per-op DM without threading it through every method signature.
/// </summary>
/// <remarks>
/// <para>
/// Backed by <see cref="AsyncLocal{T}"/>; the value flows through
/// <c>async/await</c> via <c>ExecutionContext</c>. Same idiom as
/// <see cref="System.Diagnostics.Activity.Current"/>.
/// </para>
/// <para>
/// Register as a singleton — the per-op separation comes from the
/// underlying <see cref="AsyncLocal{T}"/>, not from the service lifetime.
/// </para>
/// </remarks>
internal sealed class DmContextAccessor
{
    // static so every accessor instance (and every test override) sees the
    // same per-op slot. Matches HttpContextAccessor's internal layout.
    private static readonly AsyncLocal<ThinClientBaseDM?> _current = new();

    /// <summary>The DM for the currently-executing op, or <see langword="null"/> if outside any scope.</summary>
    public ThinClientBaseDM? Current => _current.Value;

    /// <summary>
    /// Push <paramref name="dm"/> as the current ambient DM until the
    /// returned token is disposed. Stack-style nesting is supported —
    /// dispose restores whatever was current before this <c>BeginScope</c>.
    /// Mirrors the <see cref="System.Diagnostics.Activity"/> Start / Stop
    /// pair (parent-restore on dispose).
    /// </summary>
    public IDisposable BeginScope(ThinClientBaseDM dm)
    {
        ArgumentNullException.ThrowIfNull(dm);

        var previous = _current.Value;
        _current.Value = dm;
        return new Scope(previous);
    }

    /// <summary>
    /// Restore-on-dispose token. Idempotent — double-dispose is a no-op
    /// (caller might wrap in <c>using</c> AND manually dispose; mirror
    /// .NET BCL convention).
    /// </summary>
    private sealed class Scope(ThinClientBaseDM? previous) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _current.Value = previous;
        }
    }
}
