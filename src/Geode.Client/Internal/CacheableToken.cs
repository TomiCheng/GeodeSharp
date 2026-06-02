namespace Geode.Client.Internal;

/// <summary>
/// Singleton sentinel marker stored in <see cref="MapEntry.Value"/> to
/// flag a special entry state (invalidated / destroyed / overflowed /
/// tombstone) sharing the same slot as a real user value. Mirrors
/// cppcache <c>CacheableToken</c>
/// (<c>cppcache/src/CacheableToken.hpp:33</c>). cppcache's wire-format
/// surface (<c>DataSerializableInternal</c>) is deferred until Phase 4+
/// when subscription / overflow paths exercise it.
/// </summary>
/// <remarks>
/// All state checks are reference-equality against the four readonly
/// statics — same shape as cppcache's <c>tombstoneToken == ptr</c>
/// pointer-equality test. Private ctor keeps the singletons unique.
/// </remarks>
internal sealed class CacheableToken
{
    /// <summary>Entry invalidated by user — key present, value cleared.</summary>
    public static readonly CacheableToken Invalid = new();

    /// <summary>Transitional delete state (concurrent delete + create handshake).</summary>
    public static readonly CacheableToken Destroyed = new();

    /// <summary>Value spilled to disk via <see cref="LocalRegion._persistenceManager"/>.</summary>
    public static readonly CacheableToken Overflowed = new();

    /// <summary>Deleted-but-not-collected — kept for distributed concurrency-checks version history.</summary>
    public static readonly CacheableToken Tombstone = new();

    private CacheableToken() { }

    public static bool IsInvalid(object? value) => ReferenceEquals(value, Invalid);
    public static bool IsDestroyed(object? value) => ReferenceEquals(value, Destroyed);
    public static bool IsOverflowed(object? value) => ReferenceEquals(value, Overflowed);
    public static bool IsTombstone(object? value) => ReferenceEquals(value, Tombstone);

    /// <summary>Any of the four sentinel tokens (mirrors cppcache <c>isToken</c>).</summary>
    public static bool IsToken(object? value) =>
        IsInvalid(value) || IsDestroyed(value) || IsOverflowed(value) || IsTombstone(value);
    public long ObjectSize { get; } = 4;
}
