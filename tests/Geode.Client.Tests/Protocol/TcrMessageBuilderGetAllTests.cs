using Geode.Client.Protocol;
using Geode.Client.Tests.Protocol.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Geode.Client.Protocol.Serialization;
using Xunit;

namespace Geode.Client.Tests.Protocol;

/// <summary>
/// Wire-shape unit tests for the <c>GetAll70(100)</c> request frame.
/// Mirrors cppcache <c>TcrMessageGetAll</c>
/// (<c>cppcache/src/TcrMessage.cpp:2470-2523</c>) used by
/// <c>ThinClientRegion::getAllNoThrow_remote</c>.
/// </summary>
/// <remarks>
/// Three tests bootstrap the suite — header / part-count shape,
/// full per-part wire-byte layout (incl. the inline
/// <c>CacheableObjectArray</c>-with-Java-class-header keys section),
/// and the empty-keys arg-validation guard. Encode round-trip
/// happy-path is covered by the <c>RegionGetAllIntegrationTests</c>
/// against a real Apache Geode server.
/// </remarks>
public class TcrMessageBuilderGetAllTests
{
    private static TcrMessageBuilder NewBuilder()
    {
        var sp = SerializationTestHelpers.BuildSp();
        return new(new TcrPartBuilder(sp), sp.GetRequiredService<SerializationRegistry>(), sp);
    }

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
    /// <see cref="MessageType.GetAll70"/>=100, NumParts is always
    /// <c>3</c> regardless of key count (region + inline-keys-section
    /// + callback-or-int-zero; cppcache's
    /// <c>writeHeader(m_msgType, 3)</c> is fixed at 3),
    /// TransactionId defaults to
    /// <see cref="TcrMessageBuilder.MetaTransactionId"/>, EarlyAck is
    /// <c>0</c>.
    /// </summary>
    [Fact]
    public void GetAll_emits_header_with_GetAll70_type_and_3_parts()
    {
        var keys = new object[] { 11, 22, 33 };

        var msg = NewBuilder().GetAll("/test", keys);

        Assert.Equal(MessageType.GetAll70, msg.MessageType);
        Assert.Equal(3, msg.Parts.Count);
        Assert.Equal(TcrMessageBuilder.MetaTransactionId, msg.TransactionId);
        Assert.Equal(0, msg.EarlyAck);
    }

    // ====================================================================
    //  Test 2 — Full per-part wire layout
    // ====================================================================

    /// <summary>
    /// Walks all 3 parts of a 2-key getAll (region / inline
    /// CacheableObjectArray of keys / int(0) callback placeholder)
    /// against the exact wire bytes cppcache emits.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Part 2 is the wire shape of a <c>CacheableObjectArray</c>
    /// (DSCode 52) inlined into a part</b>, not the keys serialised as
    /// individual parts. cppcache labels this "will do manually"
    /// (<c>TcrMessage.cpp:2516</c>) — the inline pattern is identical
    /// to what <c>ObjectArrayDataConverter</c> emits, but the builder
    /// writes it directly so the wire bytes are explicit at the
    /// builder site.
    /// </para>
    /// <para>
    /// <b>Java class header</b> is the literal string
    /// <c>"java.lang.Object"</c> written through cppcache's
    /// <c>DataOutput::writeString</c>
    /// (<c>cppcache/include/geode/DataOutput.hpp:264-305</c>) which
    /// itself emits a DSCode prefix —
    /// <see cref="DSCode.CacheableASCIIString"/> (87) for ASCII +
    /// u16 BE length-prefix <c>0x00 0x10</c> + 16 ASCII bytes. The
    /// DSCode prefix is part of the wire shape; cppcache's
    /// <c>writeString</c> is not the bare <c>writeUTF</c>.
    /// </para>
    /// </remarks>
    [Fact]
    public void GetAll_parts_layout_matches_cppcache_wire_order()
    {
        var keys = new object[] { 11, 22 };

        var msg = NewBuilder().GetAll("/test", keys);

        // Part 1 — Region name (raw ASCII, IsObject=0).
        Assert.Equal((byte)0, msg.Parts[0].IsObject);
        Assert.Equal("/test"u8.ToArray(), msg.Parts[0].Payload.ToArray());

        // Part 2 — Inline CacheableObjectArray of keys (IsObject=1).
        //   Layout (cppcache TcrMessage.cpp:702-710 → DataOutput.hpp:274-305):
        //     [DSCode.CacheableObjectArray=52]
        //     [ArrayLen=2 (1-byte VL)]
        //     [DSCode.Class=43]
        //     [DSCode.CacheableASCIIString=87]         ← writeString adds DSCode
        //     [u16 BE length=0x00 0x10]                "java.lang.Object" bytes
        //     [DSCode.CacheableInt32=57][i32 BE=11]    key 1
        //     [DSCode.CacheableInt32=57][i32 BE=22]    key 2
        Assert.Equal((byte)1, msg.Parts[1].IsObject);

        var javaObjectClassName = "java.lang.Object"u8.ToArray();
        var expectedPart2 = new List<byte>
        {
            DSCode.CacheableObjectArray,
            0x02,                                                    // ArrayLen = 2 (1-byte VL)
            DSCode.Class,
            DSCode.CacheableASCIIString,                             // 87 — cppcache writeString DSCode prefix
            0x00, (byte)javaObjectClassName.Length,                  // u16 BE = 16
        };
        expectedPart2.AddRange(javaObjectClassName);
        expectedPart2.AddRange(EncodedInt32(11));
        expectedPart2.AddRange(EncodedInt32(22));

        Assert.Equal(expectedPart2.ToArray(), msg.Parts[1].Payload.ToArray());

        // Part 3 — Callback placeholder = i32 BE 0 (IsObject=0).
        //   cppcache InitializeGetallMsg (TcrMessage.cpp:2517-2521)
        //   writes writeIntPart(0) when no callback. Note IsObject=0
        //   here (int part), NOT the DSCode-tagged object-part shape
        //   the callback overload uses; the server picks the right
        //   reader based on the message type, so the two shapes are
        //   not interchangeable.
        Assert.Equal((byte)0, msg.Parts[2].IsObject);
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, msg.Parts[2].Payload.ToArray());
    }

    // ====================================================================
    //  Test 3 — Arg validation
    // ====================================================================

    /// <summary>
    /// Empty <c>keys</c> is rejected with
    /// <see cref="ArgumentException"/>; the round-trip would be a
    /// no-op and cppcache's <c>writeArrayLen(0)</c> would ship
    /// a zero-length keys array that the server then has to handle
    /// as a no-op anyway. Reject up front.
    /// </summary>
    [Fact]
    public void GetAll_throws_for_empty_keys()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            NewBuilder().GetAll("/test", Array.Empty<object>()));
        Assert.Equal("keys", ex.ParamName);
    }
}
