namespace Geode.Client.Internal;

/// <summary>
/// Entry-level lifecycle event tag — identifies which
/// <c>CacheWriter</c> / <c>CacheListener</c> callback method the
/// pipeline should dispatch into for a given entry op. Mirrors
/// cppcache <c>EntryEventType</c>
/// (<c>cppcache/src/EventType.hpp:26-35</c>).
/// </summary>
/// <remarks>
/// <para>
/// Consumed by Phase 2+ writer / listener hooks — each <c>RegionAction</c>
/// strategy (Put / Create / Destroy / Invalidate) declares its
/// <c>BeforeEventType</c> + <c>AfterEventType</c>, and the shared
/// <c>updateNoThrow</c>-style pipeline reads them to pick the
/// matching callback (<c>beforeUpdate</c> vs <c>beforeCreate</c>,
/// etc.) on the user's writer / listener.
/// </para>
/// <para>
/// Phase 1.x: no consumer. Enum exists so the
/// <see cref="RegionAction"/> contract can declare the properties
/// against a real type rather than parking as <see langword="object"/>.
/// Naming: cppcache <c>SCREAMING_SNAKE_CASE</c> → C# <c>PascalCase</c>;
/// per-member xmldoc carries the cppcache <c>BEFORE_*</c> /
/// <c>AFTER_*</c> origin for grep parity.
/// </para>
/// <para>
/// Region-level events (<c>invalidateRegion</c> / <c>destroyRegion</c>
/// / <c>clear</c>) use the sibling <see cref="RegionEventType"/> enum.
/// </para>
/// </remarks>
internal enum EntryEventType
{
    /// <summary>cppcache <c>BEFORE_CREATE</c>: writer veto point before <c>Create</c>.</summary>
    BeforeCreate = 0,

    /// <summary>cppcache <c>BEFORE_UPDATE</c>: writer veto point before <c>Put</c> on an existing entry.</summary>
    BeforeUpdate,

    /// <summary>cppcache <c>BEFORE_INVALIDATE</c>: writer veto point before <c>Invalidate</c>.</summary>
    BeforeInvalidate,

    /// <summary>cppcache <c>BEFORE_DESTROY</c>: writer veto point before <c>Destroy</c> / <c>Remove</c>.</summary>
    BeforeDestroy,

    /// <summary>cppcache <c>AFTER_CREATE</c>: listener notification after a new entry is created.</summary>
    AfterCreate,

    /// <summary>cppcache <c>AFTER_UPDATE</c>: listener notification after an existing entry is updated.</summary>
    AfterUpdate,

    /// <summary>cppcache <c>AFTER_INVALIDATE</c>: listener notification after an entry's value is invalidated.</summary>
    AfterInvalidate,

    /// <summary>cppcache <c>AFTER_DESTROY</c>: listener notification after an entry is destroyed / removed.</summary>
    AfterDestroy,
}
