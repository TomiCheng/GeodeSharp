using Geode.Client.Protocol;

namespace Geode.Client.Internal;

/// <summary>
/// Contract implemented by user value classes that wish to participate
/// in delta propagation — only the changed bytes go over the wire and
/// are reapplied on the receiver, instead of the full serialised value.
/// Mirrors cppcache <c>Delta</c>
/// (<c>cppcache/include/geode/Delta.hpp:42-83</c>).
/// </summary>
/// <remarks>
/// Internal for now per CLAUDE.md 預設 internal 慣例 — no consumer
/// exists Phase 1.x. Promote to <c>public</c> in <c>Geode.Client</c>
/// (alongside making <see cref="DataInput"/> / <see cref="DataOutput"/>
/// public) when Phase 4+ subscription + delta propagation lands and user
/// value classes need to implement this.
/// </remarks>
internal interface IDelta: ICloneable
{
    /// <summary>
    /// Whether this object carries an applicable delta. cppcache
    /// <c>Delta::hasDelta</c> (<c>Delta.hpp:61</c>) — called on
    /// <c>Region::put</c> to decide whether to send a delta or the full
    /// object.
    /// </summary>
    bool HasDelta();

    /// <summary>
    /// Serialise this object's delta to <paramref name="output"/>.
    /// cppcache <c>Delta::toDelta</c> (<c>Delta.hpp:70</c>) — invoked
    /// when <see cref="HasDelta"/> returns <see langword="true"/>.
    /// </summary>
    void ToDelta(DataOutput output);

    /// <summary>
    /// Apply a delta read from <paramref name="input"/> to this object
    /// in place. cppcache <c>Delta::fromDelta</c>
    /// (<c>Delta.hpp:83</c>) — receiver-side counterpart of
    /// <see cref="ToDelta"/>. Throwing
    /// <c>InvalidDeltaException</c> (cppcache) /
    /// <c>GfErrTypeException(InvalidDelta)</c> (C# port) signals the
    /// receiver to fall back to a full-object fetch.
    /// </summary>
    void FromDelta(DataInput input);
}
