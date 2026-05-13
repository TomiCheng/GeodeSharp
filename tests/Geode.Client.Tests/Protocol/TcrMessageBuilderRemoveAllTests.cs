using Geode.Client.Protocol;
using Geode.Client.Tests.Protocol.Serialization;
using Xunit;

namespace Geode.Client.Tests.Protocol;

/// <summary>
/// Wire-shape unit tests for the <c>RemoveAll(109)</c> request frame.
/// Mirrors cppcache <c>TcrMessageRemoveAll</c>
/// (<c>cppcache/src/TcrMessage.cpp:2424-2468</c>) used by
/// <c>ThinClientRegion::multiHopRemoveAllNoThrow_remote</c>.
/// </summary>
/// <remarks>
/// Three tests bootstrap the suite — header / part-count shape,
/// full per-part wire-byte layout, and the empty-keys arg-validation
/// guard. The full suite (encode round-trip, callback variants,
/// unregistered-type keys) lands later; the round-trip happy-path is
/// already covered by the <c>RegionRemoveAllIntegrationTests</c>
/// against a real Apache Geode server.
/// </remarks>
public class TcrMessageBuilderRemoveAllTests
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
    /// <see cref="MessageType.RemoveAll"/>=109, NumParts is
    /// <c>5 + keys.Count</c> (callback always emitted as
    /// <see cref="DSCode.NullObj"/> &#x2014; no optional-callback
    /// branch like <c>Destroy</c> / <c>Invalidate</c>), TransactionId
    /// defaults to <see cref="TcrMessageBuilder.MetaTransactionId"/>,
    /// EarlyAck is <c>0</c>.
    /// </summary>
    [Fact]
    public void RemoveAll_emits_header_with_RemoveAll_type_and_5plusN_parts()
    {
        var keys = new object[] { 11, 22, 33 };

        var msg = NewBuilder().RemoveAll("/test", keys, ThreadId, BaseSeqId);

        Assert.Equal(MessageType.RemoveAll, msg.MessageType);
        Assert.Equal(5 + keys.Length, msg.Parts.Count);
        Assert.Equal(TcrMessageBuilder.MetaTransactionId, msg.TransactionId);
        Assert.Equal(0, msg.EarlyAck);
    }

    // ====================================================================
    //  Test 2 — Full per-part wire layout
    // ====================================================================

    /// <summary>
    /// Walks all 7 parts of a 2-key removeAll (region / eventId /
    /// flags / callback-NullObj / keyCount / key1 / key2) against the
    /// exact wire bytes cppcache emits. Region is raw ASCII, eventId
    /// is 18 raw bytes with the longCode-prefix layout, flags is
    /// <c>i32 BE = 0</c> (Phase 1.3 MVP), callback ships
    /// <see cref="DSCode.NullObj"/> when no caller-supplied callback
    /// (cppcache <c>writeObjectPart(nullptr)</c>), keyCount is
    /// <c>i32 BE</c>, each key is DSCode-tagged through the registry.
    /// </summary>
    [Fact]
    public void RemoveAll_parts_layout_matches_cppcache_wire_order()
    {
        // Use byte-pattern eventId so each byte position is unambiguous.
        const long Tid = 0x0102030405060708L;
        const long Seq = 0x090A0B0C0D0E0F10L;
        var keys = new object[] { 11, 22 };

        var msg = NewBuilder().RemoveAll("/test", keys, Tid, Seq);

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

        // Part 3 — Flags (i32 BE = 0, IsObject=0). Phase 1.3 MVP
        //   regions don't expose caching-enabled / concurrency-checks,
        //   so the flag bits stay 0.
        Assert.Equal((byte)0, msg.Parts[2].IsObject);
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, msg.Parts[2].Payload.ToArray());

        // Part 4 — Callback (always emitted, IsObject=1). cppcache
        //   writeObjectPart(nullptr) writes DSCode.NullObj rather than
        //   skipping the part — the part count is unconditionally
        //   5+N regardless of caller-supplied callback. Verifies the
        //   1-byte NullObj payload.
        Assert.Equal((byte)1, msg.Parts[3].IsObject);
        Assert.Equal(new byte[] { DSCode.NullObj }, msg.Parts[3].Payload.ToArray());

        // Part 5 — KeyCount (i32 BE = 2, IsObject=0).
        Assert.Equal((byte)0, msg.Parts[4].IsObject);
        Assert.Equal(new byte[] { 0, 0, 0, 2 }, msg.Parts[4].Payload.ToArray());

        // Parts 6, 7 — keys, each DSCode-tagged (IsObject=1).
        Assert.Equal((byte)1, msg.Parts[5].IsObject);
        Assert.Equal(EncodedInt32(11), msg.Parts[5].Payload.ToArray());

        Assert.Equal((byte)1, msg.Parts[6].IsObject);
        Assert.Equal(EncodedInt32(22), msg.Parts[6].Payload.ToArray());
    }

    // ====================================================================
    //  Test 3 — Arg validation
    // ====================================================================

    /// <summary>
    /// Empty <c>keys</c> is rejected. cppcache writes
    /// <c>writeEventIdPart(keys.size() - 1)</c> &#x2014; on zero size
    /// the <c>uint32_t</c> arithmetic underflows; the round-trip is a
    /// no-op anyway. Our builder rejects up front with
    /// <see cref="ArgumentException"/> so misuse surfaces at the
    /// call site, not at the server.
    /// </summary>
    [Fact]
    public void RemoveAll_throws_for_empty_keys()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            NewBuilder().RemoveAll("/test", Array.Empty<object>(), ThreadId, BaseSeqId));
        Assert.Equal("keys", ex.ParamName);
    }
}
