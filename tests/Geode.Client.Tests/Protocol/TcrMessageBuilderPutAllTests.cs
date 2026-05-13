using Geode.Client.Protocol;
using Geode.Client.Tests.Protocol.Serialization;
using Xunit;

namespace Geode.Client.Tests.Protocol;

/// <summary>
/// Wire-shape unit tests for the <c>PutAll(56)</c> request frame.
/// Mirrors cppcache <c>TcrMessagePutAll</c>
/// (<c>cppcache/src/TcrMessage.cpp:2354-2422</c>) used by
/// <c>ThinClientRegion::multiHopPutAllNoThrow_remote</c>.
/// </summary>
/// <remarks>
/// Three tests bootstrap the suite — header / part-count shape,
/// full per-part wire-byte layout (incl. the SkipCallbacks placeholder
/// quirk), and the empty-map arg-validation guard. Encode round-trip
/// happy-path is covered by the <c>RegionPutAllIntegrationTests</c>
/// against a real Apache Geode server.
/// </remarks>
public class TcrMessageBuilderPutAllTests
{
    private const long ThreadId = 1L;
    private const long BaseSeqId = 100L;

    private static TcrMessageBuilder NewBuilder() =>
        new(new TcrPartBuilder(), SerializationTestHelpers.CreateRegistry());

    private static byte[] EncodedInt32(int v) =>
    [
        DSCode.CacheableInt32,
        (byte)((v >> 24) & 0xFF),
        (byte)((v >> 16) & 0xFF),
        (byte)((v >> 8) & 0xFF),
        (byte)(v & 0xFF),
    ];

    // ====================================================================
    //  Test 1 — Header + Part-count shape
    // ====================================================================

    /// <summary>
    /// Verifies the 4 header invariants in one shot: MessageType is
    /// <see cref="MessageType.PutAll"/>=56, NumParts is
    /// <c>5 + map.Count*2</c> (5 fixed parts + 2 per entry; the
    /// callback path lands on <see cref="MessageType.PutAllWithCallback"/>
    /// = 108 with 6+2N parts — not exercised here, Phase 1.3 has no
    /// callback overload), TransactionId defaults to
    /// <see cref="TcrMessageBuilder.MetaTransactionId"/>, EarlyAck is
    /// <c>0</c>.
    /// </summary>
    [Fact]
    public void PutAll_emits_header_with_PutAll_type_and_5plus2N_parts()
    {
        var map = new Dictionary<object, object>
        {
            [11] = 1111,
            [22] = 2222,
            [33] = 3333,
        };

        var msg = NewBuilder().PutAll("/test", map, ThreadId, BaseSeqId);

        Assert.Equal(MessageType.PutAll, msg.MessageType);
        Assert.Equal(5 + map.Count * 2, msg.Parts.Count);
        Assert.Equal(TcrMessageBuilder.MetaTransactionId, msg.TransactionId);
        Assert.Equal(0, msg.EarlyAck);
    }

    // ====================================================================
    //  Test 2 — Full per-part wire layout
    // ====================================================================

    /// <summary>
    /// Walks all 9 parts of a 2-entry putAll (region / eventId /
    /// skipCallbacks=0 / flags=0 / count / key1 / val1 / key2 / val2)
    /// against the exact wire bytes cppcache emits.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Two consecutive int-parts after EventId</b> is the PutAll
    /// quirk: cppcache writes
    /// <c>writeIntPart(0)</c> (commented as
    /// <c>writeIntPart(skipCallBacks ? 0 : 1)</c> — placeholder that
    /// was hard-coded to 0) followed by
    /// <c>writeIntPart(flags)</c>. Part 3 is therefore <i>not</i> the
    /// flags part — it's the skipCallbacks placeholder. Part 4 is
    /// the real flags. This test pins the byte order against
    /// regression.
    /// </para>
    /// <para>
    /// Uses an ordered <see cref="LinkedList{T}"/>-backed dictionary
    /// shape — <see cref="Dictionary{TKey, TValue}"/> insertion-
    /// preserving iteration is documented behaviour on modern .NET,
    /// so feeding (11→1111, 22→2222) gives a deterministic part
    /// ordering for byte comparison.
    /// </para>
    /// </remarks>
    [Fact]
    public void PutAll_parts_layout_matches_cppcache_wire_order()
    {
        // Use byte-pattern eventId so each byte position is unambiguous.
        const long Tid = 0x0102030405060708L;
        const long Seq = 0x090A0B0C0D0E0F10L;

        // Dictionary insertion-order iteration is documented; this gives
        // a deterministic part-order for byte-level comparison.
        var map = new Dictionary<object, object>
        {
            [11] = 1111,
            [22] = 2222,
        };

        var msg = NewBuilder().PutAll("/test", map, Tid, Seq);

        // Part 1 — Region name (raw ASCII, IsObject=0).
        Assert.Equal((byte)0, msg.Parts[0].IsObject);
        Assert.Equal("/test"u8.ToArray(), msg.Parts[0].Payload.ToArray());

        // Part 2 — EventId (18 raw bytes, IsObject=0):
        //   [longCode=3][i64 threadId BE][longCode=3][i64 baseSeq BE]
        Assert.Equal((byte)0, msg.Parts[1].IsObject);
        Assert.Equal(
            new byte[] {
                0x03, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
                0x03, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10,
            },
            msg.Parts[1].Payload.ToArray());

        // Part 3 — SkipCallbacks placeholder (i32 BE = 0, IsObject=0).
        //   cppcache hard-codes 0 (commented as
        //   `writeIntPart(skipCallBacks ? 0 : 1)`). Mirrors byte-for-byte.
        Assert.Equal((byte)0, msg.Parts[2].IsObject);
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, msg.Parts[2].Payload.ToArray());

        // Part 4 — Flags (i32 BE = 0, IsObject=0). Phase 1.3 MVP
        //   regions don't expose caching-enabled / concurrency-checks,
        //   so the flag bits stay 0.
        Assert.Equal((byte)0, msg.Parts[3].IsObject);
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, msg.Parts[3].Payload.ToArray());

        // Part 5 — Entry count (i32 BE = 2, IsObject=0).
        Assert.Equal((byte)0, msg.Parts[4].IsObject);
        Assert.Equal(new byte[] { 0, 0, 0, 2 }, msg.Parts[4].Payload.ToArray());

        // Parts 6-9 — alternating (key, value), each DSCode-tagged
        //   (IsObject=1). cppcache's HashMapOfCacheable iteration
        //   matches .NET Dictionary insertion order, so we assert
        //   the iteration we fed in: (11,1111), (22,2222).
        Assert.Equal((byte)1, msg.Parts[5].IsObject);
        Assert.Equal(EncodedInt32(11), msg.Parts[5].Payload.ToArray());

        Assert.Equal((byte)1, msg.Parts[6].IsObject);
        Assert.Equal(EncodedInt32(1111), msg.Parts[6].Payload.ToArray());

        Assert.Equal((byte)1, msg.Parts[7].IsObject);
        Assert.Equal(EncodedInt32(22), msg.Parts[7].Payload.ToArray());

        Assert.Equal((byte)1, msg.Parts[8].IsObject);
        Assert.Equal(EncodedInt32(2222), msg.Parts[8].Payload.ToArray());
    }

    // ====================================================================
    //  Test 3 — Arg validation
    // ====================================================================

    /// <summary>
    /// Empty <c>map</c> is rejected. cppcache writes
    /// <c>writeEventIdPart(map.size() - 1)</c> &#x2014; underflow on
    /// zero size; the round-trip is a no-op anyway. Our builder
    /// rejects up front with <see cref="ArgumentException"/>.
    /// </summary>
    [Fact]
    public void PutAll_throws_for_empty_map()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            NewBuilder().PutAll(
                "/test",
                new Dictionary<object, object>(),
                ThreadId,
                BaseSeqId));
        Assert.Equal("map", ex.ParamName);
    }
}
