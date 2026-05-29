namespace Geode.Client;

/// <summary>
/// User-implemented hook invoked <b>before</b> a mutating cache op, able to
/// veto it by returning <see langword="false"/>.
/// </summary>
public interface ICacheWriter
{
    /// <summary>Before a new entry is created; return <see langword="false"/> to veto.</summary>
    ValueTask<bool> BeforeCreateAsync(EntryEvent ev, CancellationToken ct = default) => new(true);

    /// <summary>Before an existing entry is updated; return <see langword="false"/> to veto.</summary>
    ValueTask<bool> BeforeUpdateAsync(EntryEvent ev, CancellationToken ct = default) => new(true);

    /// <summary>Before an entry is destroyed / removed; return <see langword="false"/> to veto.</summary>
    ValueTask<bool> BeforeDestroyAsync(EntryEvent ev, CancellationToken ct = default) => new(true);

    /// <summary>Before the region is cleared; return <see langword="false"/> to veto.</summary>
    ValueTask<bool> BeforeRegionClearAsync(RegionEvent ev, CancellationToken ct = default) => new(true);

    /// <summary>Before the region is destroyed; return <see langword="false"/> to veto.</summary>
    ValueTask<bool> BeforeRegionDestroyAsync(RegionEvent ev, CancellationToken ct = default) => new(true);

    /// <summary>When the writer is detached or the cache is closed.</summary>
    ValueTask CloseAsync(IRegion region, CancellationToken ct = default) => default;
}
