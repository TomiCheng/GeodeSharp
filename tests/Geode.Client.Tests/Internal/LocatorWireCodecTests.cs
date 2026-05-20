using System.Buffers;
using Geode.Client.Internal;
using Geode.Client.Protocol;
using Geode.Client.Tests.Protocol.Serialization;
using Xunit;

namespace Geode.Client.Tests.Internal;

/// <summary>
/// Byte-fixture tests for the locator wire codec. Each test pins the
/// exact bytes a cppcache locator would produce / consume so a future
/// edit to <see cref="DataOutput.WriteString"/> or the
/// DSCode constants doesn't silently break locator interop.
/// </summary>
public class LocatorWireCodecTests
{
    // ─────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────

    private static byte[] Write(Action<DataOutput> body)
    {
        using var writer = new DataOutput(SerializationTestHelpers.CreateRegistry());
        body(writer);
        return writer.WrittenSpan.ToArray();
    }

    /// <summary>Encode <paramref name="s"/> the way cppcache <c>writeString</c> does for ASCII input: <c>[CacheableASCIIString=87][u16 length][bytes]</c>.</summary>
    private static byte[] EncodedAsciiString(string s)
    {
        var bytes = new byte[3 + s.Length];
        bytes[0] = DSCode.CacheableASCIIString;
        bytes[1] = (byte)((s.Length >> 8) & 0xFF);
        bytes[2] = (byte)(s.Length & 0xFF);
        for (var i = 0; i < s.Length; i++) bytes[3 + i] = (byte)s[i];
        return bytes;
    }

    private static byte[] EncodedInt32BigEndian(int v) =>
    [
        (byte)((v >> 24) & 0xFF),
        (byte)((v >> 16) & 0xFF),
        (byte)((v >> 8) & 0xFF),
        (byte)(v & 0xFF),
    ];

    // ─────────────────────────────────────────────────────────────
    //  LocatorListRequest
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void LocatorListRequest_defaults_server_group_to_empty()
    {
        Assert.Equal("", new LocatorListRequest().ServerGroup);
    }

    [Fact]
    public void LocatorListRequest_writes_empty_server_group_as_zero_length_ascii_string()
    {
        var bytes = Write(w => new LocatorListRequest("").WriteTo(w));

        Assert.Equal([DSCode.CacheableASCIIString, 0, 0], bytes);
    }

    [Fact]
    public void LocatorListRequest_writes_servergroup_as_ascii_string()
    {
        var bytes = Write(w => new LocatorListRequest("group1").WriteTo(w));

        Assert.Equal(EncodedAsciiString("group1"), bytes);
    }

    // ─────────────────────────────────────────────────────────────
    //  LocatorListResponse
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void LocatorListResponse_decodes_empty_list_not_balanced()
    {
        // [u32 count=0] [bool isBalanced=0]
        byte[] bytes = [0, 0, 0, 0, 0];
        var reader = new BigEndianBinaryReader(bytes);

        var response = LocatorListResponse.ReadFrom(reader);

        Assert.Empty(response.Locators);
        Assert.False(response.IsBalanced);
    }

    [Fact]
    public void LocatorListResponse_decodes_one_locator_balanced()
    {
        byte[] bytes =
        [
            // count = 1
            0, 0, 0, 1,
            // ServerLocation.fromData: readString "host" + readInt32 1234
            DSCode.CacheableASCIIString, 0, 4,
            (byte)'h', (byte)'o', (byte)'s', (byte)'t',
            0, 0, 0x04, 0xD2,                          // 1234 BE
            // isBalanced = true
            1,
        ];
        var reader = new BigEndianBinaryReader(bytes);

        var response = LocatorListResponse.ReadFrom(reader);

        Assert.Single(response.Locators);
        Assert.Equal("host", response.Locators[0].Host);
        Assert.Equal(1234, response.Locators[0].Port);
        Assert.True(response.IsBalanced);
    }

    [Fact]
    public void LocatorListResponse_decodes_multiple_locators()
    {
        byte[] bytes =
        [
            0, 0, 0, 2,
            DSCode.CacheableASCIIString, 0, 3, (byte)'a', (byte)'a', (byte)'a',
            0, 0, 0x27, 0x10,                          // 10000
            DSCode.CacheableASCIIString, 0, 3, (byte)'b', (byte)'b', (byte)'b',
            0, 0, 0x4E, 0x20,                          // 20000
            0,
        ];
        var reader = new BigEndianBinaryReader(bytes);

        var response = LocatorListResponse.ReadFrom(reader);

        Assert.Equal(2, response.Locators.Count);
        Assert.Equal(new ServerLocation("aaa", 10000), response.Locators[0]);
        Assert.Equal(new ServerLocation("bbb", 20000), response.Locators[1]);
        Assert.False(response.IsBalanced);
    }

    [Fact]
    public void LocatorListResponse_throws_on_negative_count()
    {
        // u32 0xFFFFFFFF read as signed int32 = -1 → wire corruption
        byte[] bytes = [0xFF, 0xFF, 0xFF, 0xFF];
        var reader = new BigEndianBinaryReader(bytes);

        var ex = Assert.Throws<GeodeException>(() => LocatorListResponse.ReadFrom(reader));
        Assert.Contains("negative locator count", ex.Message);
    }

    // ─────────────────────────────────────────────────────────────
    //  ClientConnectionRequest
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ClientConnectionRequest_writes_servergroup_then_empty_exclude_set()
    {
        var bytes = Write(w =>
            new ClientConnectionRequest("group1", []).WriteTo(w));

        // [writeString "group1"][i32 setSize=0]
        var expected = new List<byte>();
        expected.AddRange(EncodedAsciiString("group1"));
        expected.AddRange(EncodedInt32BigEndian(0));
        Assert.Equal(expected.ToArray(), bytes);
    }

    [Fact]
    public void ClientConnectionRequest_writes_excluded_servers_in_iteration_order()
    {
        ServerLocation[] excluded =
        [
            new("dead1", 40404),
            new("dead2", 40405),
        ];
        var bytes = Write(w =>
            new ClientConnectionRequest("", excluded).WriteTo(w));

        // [writeString ""][i32 setSize=2]
        //   [writeString "dead1"][i32 40404]
        //   [writeString "dead2"][i32 40405]
        var expected = new List<byte>();
        expected.AddRange(EncodedAsciiString(""));
        expected.AddRange(EncodedInt32BigEndian(2));
        expected.AddRange(EncodedAsciiString("dead1"));
        expected.AddRange(EncodedInt32BigEndian(40404));
        expected.AddRange(EncodedAsciiString("dead2"));
        expected.AddRange(EncodedInt32BigEndian(40405));
        Assert.Equal(expected.ToArray(), bytes);
    }

    // ─────────────────────────────────────────────────────────────
    //  ClientConnectionResponse
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ClientConnectionResponse_decodes_server_not_found_as_no_body()
    {
        // [bool serverFound=0]  — no ServerLocation pair follows
        byte[] bytes = [0];
        var reader = new BigEndianBinaryReader(bytes);

        var response = ClientConnectionResponse.ReadFrom(reader);

        Assert.False(response.ServerFound);
        Assert.Null(response.Server);
    }

    [Fact]
    public void ClientConnectionResponse_decodes_server_found_with_location()
    {
        // [bool=1][writeString "myhost"][i32 40404]
        byte[] bytes =
        [
            1,
            DSCode.CacheableASCIIString, 0, 6,
            (byte)'m', (byte)'y', (byte)'h', (byte)'o', (byte)'s', (byte)'t',
            0, 0, 0x9D, 0xD4,                          // 40404 BE
        ];
        var reader = new BigEndianBinaryReader(bytes);

        var response = ClientConnectionResponse.ReadFrom(reader);

        Assert.True(response.ServerFound);
        Assert.NotNull(response.Server);
        Assert.Equal("myhost", response.Server!.Host);
        Assert.Equal(40404, response.Server.Port);
    }
}
