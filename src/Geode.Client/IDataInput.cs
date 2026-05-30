namespace Geode.Client;

/// <summary>
/// Wire-side reader surface a user value class consumes when applying a
/// delta via <see cref="IDelta.FromDelta(IDataInput)"/>. Mirrors cppcache
/// <c>DataInput</c> (<c>cppcache/include/geode/DataInput.hpp</c>) as the
/// public-facing read surface.
/// </summary>
/// <remarks>
/// Empty placeholder — read primitives (<c>ReadInt32</c>,
/// <c>ReadString</c>, ...) land on this interface when delta propagation
/// goes public (Phase 4+). Until then it exists only so <see cref="IDelta"/>
/// has a public parameter type and the internal <c>DataInput</c> concrete
/// class can satisfy it.
/// </remarks>
public interface IDataInput
{
}
