namespace Geode.Client.Internal;

/// <summary>
/// Region-level lifecycle event tag — identifies which
/// <c>CacheWriter</c> / <c>CacheListener</c> callback method the
/// pipeline should dispatch into for a given region-scope op
/// (<c>invalidateRegion</c> / <c>destroyRegion</c> / <c>clear</c>).
/// Mirrors cppcache <c>RegionEventType</c>
/// (<c>cppcache/src/EventType.hpp:37-44</c>).
/// </summary>
/// <remarks>
/// <para>
/// Sibling to <see cref="EntryEventType"/>; that one covers entry-scope
/// ops (Put / Create / Destroy / Invalidate single key), this one
/// covers ops that fan out across the whole region. Consumed by
/// Phase 2+ writer / listener hooks the same way.
/// </para>
/// <para>
/// Phase 1.x: no consumer. Enum exists so the future region-event
/// callback path has a typed surface to bind against rather than
/// parking as <see langword="object"/>.
/// Naming: cppcache <c>SCREAMING_SNAKE_CASE</c> → C# <c>PascalCase</c>;
/// per-member xmldoc carries the cppcache <c>BEFORE_REGION_*</c> /
/// <c>AFTER_REGION_*</c> origin for grep parity.
/// </para>
/// </remarks>
internal enum RegionEventType
{
    /// <summary>cppcache <c>BEFORE_REGION_INVALIDATE</c>: writer veto point before <c>InvalidateRegion</c>.</summary>
    BeforeRegionInvalidate = 0,

    /// <summary>cppcache <c>BEFORE_REGION_DESTROY</c>: writer veto point before <c>DestroyRegion</c>.</summary>
    BeforeRegionDestroy,

    /// <summary>cppcache <c>AFTER_REGION_INVALIDATE</c>: listener notification after <c>InvalidateRegion</c>.</summary>
    AfterRegionInvalidate,

    /// <summary>cppcache <c>AFTER_REGION_DESTROY</c>: listener notification after <c>DestroyRegion</c>.</summary>
    AfterRegionDestroy,

    /// <summary>cppcache <c>BEFORE_REGION_CLEAR</c>: writer veto point before <c>Clear</c>.</summary>
    BeforeRegionClear,

    /// <summary>cppcache <c>AFTER_REGION_CLEAR</c>: listener notification after <c>Clear</c>.</summary>
    AfterRegionClear,
}
