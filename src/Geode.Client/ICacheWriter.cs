namespace Geode.Client;

/// <summary>
/// User-implemented hook invoked synchronously <b>before</b> a mutating
/// cache op, able to veto it by returning <see langword="false"/>.
/// </summary>
public interface ICacheWriter
{
    /// <summary>Before a new entry is created; return <see langword="false"/> to veto.</summary>
    bool BeforeCreate(EntryEvent ev) => true;

    /// <summary>Before an existing entry is updated; return <see langword="false"/> to veto.</summary>
    bool BeforeUpdate(EntryEvent ev) => true;

    /// <summary>Before an entry is destroyed / removed; return <see langword="false"/> to veto.</summary>
    bool BeforeDestroy(EntryEvent ev) => true;

    /// <summary>Before the region is cleared; return <see langword="false"/> to veto.</summary>
    bool BeforeRegionClear(RegionEvent ev) => true;

    /// <summary>Before the region is destroyed; return <see langword="false"/> to veto.</summary>
    bool BeforeRegionDestroy(RegionEvent ev) => true;

    /// <summary>When the writer is detached or the cache is closed.</summary>
    void Close(IRegion region) { }
}
