namespace Geode.Client;

/// <summary>
/// Wire-side writer surface a user value class consumes when serialising
/// a delta via <see cref="IDelta.ToDelta(IDataOutput)"/>. Mirrors cppcache
/// <c>DataOutput</c> (<c>cppcache/include/geode/DataOutput.hpp</c>) as the
/// public-facing write surface.
/// </summary>
/// <remarks>
/// Empty placeholder — write primitives (<c>WriteInt32</c>,
/// <c>WriteString</c>, ...) land on this interface when delta propagation
/// goes public (Phase 4+). Until then it exists only so <see cref="IDelta"/>
/// has a public parameter type and the internal <c>DataOutput</c> concrete
/// class can satisfy it.
/// </remarks>
public interface IDataOutput
{
}
