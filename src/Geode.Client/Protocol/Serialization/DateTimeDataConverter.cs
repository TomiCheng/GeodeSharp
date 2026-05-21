namespace Geode.Client.Protocol.Serialization;

/// <summary>
/// <see cref="IDataConverter"/> for <see cref="DateTime"/> ??/// <see cref="DSCode.CacheableDate"/> (61). Wire payload is an 8-byte
/// big-endian signed integer: milliseconds since the Unix epoch
/// (1970-01-01T00:00:00Z), matching Java
/// <c>java.util.Date.getTime()</c>. Mirrors cppcache
/// <c>CacheableDate</c> (<c>cppcache/src/CacheableDate.cpp</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Round-trip is UTC-safe.</b> <see cref="Read"/> always returns
/// a <see cref="DateTime"/> with <see cref="DateTime.Kind"/> =
/// <see cref="DateTimeKind.Utc"/>. This deliberately diverges from
/// the C++/CLI <c>clicache</c> reference implementation
/// (<c>geode-native/clicache/src/CacheableDate.cpp::FromData</c>)
/// which calls <c>ToLocalTime()</c> on read ??that introduces a
/// subtle Kind-flip footgun where
/// <c>DateTime.UtcNow ??wire ??Kind=Local</c>. We keep the instant
/// stable in UTC; callers wanting local-time display call
/// <see cref="DateTime.ToLocalTime"/> explicitly.
/// </para>
/// <para>
/// <b>Write rejects <see cref="DateTimeKind.Unspecified"/>.</b>
/// <c>DateTime.ToUniversalTime</c> silently assumes
/// Unspecified means Local ??which makes wire output depend on the
/// runtime's local timezone, a cross-host non-determinism we refuse
/// to inherit. cppcache / clicache don't model Kind at all so this
/// concern is .NET-only. Callers must set <see cref="DateTime.Kind"/>
/// explicitly via <see cref="DateTime.SpecifyKind"/> or the
/// <see cref="DateTime(int, int, int, int, int, int, DateTimeKind)"/>
/// overload.
/// </para>
/// <para>
/// <b>Precision is millisecond.</b> Sub-millisecond ticks are
/// truncated on write (no rounding) ??matches the natural .NET
/// behaviour of <see cref="DateTimeOffset.ToUnixTimeMilliseconds"/>
/// and avoids the clicache "round to nearest ms" quirk where
/// <c>t.AddTicks(1) == t</c> can become true.
/// </para>
/// </remarks>
internal sealed class DateTimeDataConverter : DataConverter<DateTime>
{
    private static readonly byte[] s_dsCodes = { DSCode.CacheableDate };

    public override byte[] DsCodes => s_dsCodes;

    public override ValueTask WriteAsync(DataOutput writer, DateTime value, byte dsCode, int depth, CancellationToken ct)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            DateTimeKind.Unspecified => throw new ArgumentException(
                "DateTime with Kind=Unspecified cannot be serialised: " +
                "the wire form is UTC milliseconds and Unspecified would " +
                "force a silent local-timezone assumption. Use " +
                "DateTime.SpecifyKind(value, DateTimeKind.Utc) or construct " +
                "with an explicit Kind.",
                nameof(value)),
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };
        long ms = (utc - DateTime.UnixEpoch).Ticks / TimeSpan.TicksPerMillisecond;
        writer.WriteInt64(ms);
        return ValueTask.CompletedTask;
    }

    public override DateTime Read(BigEndianBinaryReader reader, byte dsCode, int depth)
    {
        long ms = reader.ReadInt64();
        // DateTime.UnixEpoch is Kind=Utc; AddTicks preserves Kind.
        return DateTime.UnixEpoch.AddTicks(ms * TimeSpan.TicksPerMillisecond);
    }
}
