namespace Geode.Client.Protocol;

/// <summary>
/// Geode wire-protocol version ordinal. Mirrors
/// <c>cppcache/src/Version.hpp</c> and <c>Version.cpp</c>.
/// </summary>
/// <remarks>
/// <para>
/// Only the ordinal goes on the wire ??major/minor/patch are not part of
/// the handshake (despite what some upstream comments imply). Two encodings:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Compressed</b> (default, ordinal ??<see cref="sbyte.MaxValue"/>):
///     1 byte (i8) carrying the ordinal directly.
///   </item>
///   <item>
///     <b>Uncompressed</b> (ordinal &gt; 127): sentinel byte <c>-1</c>
///     followed by i16 ordinal (3 bytes total).
///   </item>
/// </list>
/// <para>
/// The uncompressed branch is unreachable today (<see cref="Current"/> = 125)
/// but kept in <see cref="WriteTo"/> so we don't get caught out when
/// upstream eventually crosses 127.
/// </para>
/// </remarks>
internal readonly record struct ProtocolVersion(short Ordinal)
{
    /// <summary>
    /// The ordinal this client identifies itself as on every handshake.
    /// cppcache <c>Version::current()</c> hardcodes 125 (= "Geode 1.14.0"
    /// wire protocol); any 1.14+ server is backward-compatible.
    /// </summary>
    /// <remarks>
    /// Bump this only when:
    /// <list type="bullet">
    ///   <item>We need a feature gated behind a newer ordinal.</item>
    ///   <item>The new ordinal &gt; 127 ??at which point <see cref="WriteTo"/>
    ///         starts taking the uncompressed branch; verify it's correct.</item>
    /// </list>
    /// </remarks>
    public static ProtocolVersion Current => new(125);

    /// <summary>
    /// Sentinel byte that signals "uncompressed encoding follows" in the
    /// cppcache wire format. Defined as <c>kTokenOrdinal</c> there.
    /// </summary>
    private const sbyte TokenOrdinal = -1;

    /// <summary>
    /// Append this version to <paramref name="writer"/> using the cppcache
    /// <c>Version::write</c> wire format.
    /// </summary>
    public void WriteTo(DataOutput writer)
    {
        if (Ordinal <= sbyte.MaxValue)
        {
            // Compressed form: ordinal fits in i8, write it directly.
            writer.WriteSByte((sbyte)Ordinal);
        }
        else
        {
            // Uncompressed form: sentinel byte tells the peer "an i16
            // ordinal follows". Unreachable today (Current = 125), kept
            // honest so a future bump past 127 just works.
            writer.WriteSByte(TokenOrdinal);
            writer.WriteInt16(Ordinal);
        }
    }
}
