using System.Buffers.Binary;
using System.Net;
using System.Text;
using Geode.Client.Internal;
using Geode.Client.Options;
using Geode.Client.Protocol;
using Xunit;

namespace Geode.Client.Tests.Protocol;

/// <summary>
/// Verifies the byte layout of the identity blob produced by
/// <see cref="ClientProxyMembershipIdBuilder"/> against the schema
/// expected by Java <c>MemberIdentifierImpl.fromDataPre_GFE_9_0_0_0</c>.
/// </summary>
/// <remarks>
/// We can't lock in exact bytes (hostname / IP / PID vary per machine),
/// so the tests parse the blob and assert the structure + recovered
/// values. The parser doubles as a regression detector — if a future
/// change drops or reorders a field, parsing throws or asserts fail.
/// </remarks>
public class ClientProxyMembershipIdBuilderTests
{
    private static ClientProxyMembershipIdBuilder NewBuilder(GeodeClientOptions? options = null)
    {
        var ctx = new CacheScopeContext();
        ctx.Initialize(string.Empty, options ?? new GeodeClientOptions());
        return new ClientProxyMembershipIdBuilder(ctx);
    }

    // ====================================================================
    //  Smoke / invariants
    // ====================================================================

    [Fact]
    public void Build_returns_non_empty_byte_array()
    {
        var bytes = NewBuilder().Build();
        Assert.NotEmpty(bytes);
    }

    [Fact]
    public void Build_is_idempotent_returns_same_array_reference()
    {
        var b = NewBuilder();
        var first = b.Build();
        var second = b.Build();
        Assert.Same(first, second);
    }

    [Fact]
    public void Build_starts_with_FixedIdByte_then_InternalDistributedMember_DSFid()
    {
        var bytes = NewBuilder().Build();
        Assert.Equal(1, bytes[0]);    // FixedIDByte
        Assert.Equal(92, bytes[1]);   // DSFid InternalDistributedMember
    }

    // ====================================================================
    //  Full structural decode against default options
    // ====================================================================

    [Fact]
    public void Build_full_schema_default_options()
    {
        var parsed = MembershipBlob.Parse(NewBuilder().Build());

        Assert.Equal(1, parsed.FixedIdByte);
        Assert.Equal(92, parsed.Dsfid);

        // IPv4 = 4 bytes, IPv6 = 16 bytes — neither is empty.
        Assert.True(parsed.HostAddress.Length is 4 or 16,
            $"Expected IPv4 (4) or IPv6 (16) bytes, got {parsed.HostAddress.Length}");

        Assert.Equal(0, parsed.SyncCounter);
        Assert.Equal(Dns.GetHostName(), parsed.Hostname);
        Assert.Equal(0, parsed.SplitBrainFlag);
        Assert.Equal(12334, parsed.DcPort);                      // cppcache kDcPort
        Assert.Equal(Environment.ProcessId, parsed.VmPid);
        Assert.Equal(13, parsed.VmKind);                          // VmKindLoner
        Assert.Equal(0, parsed.RoleArrayLen);
        Assert.Equal(string.Empty, parsed.DsName);                // GeodeClientOptions.Name default
        Assert.StartsWith("Native_", parsed.UniqueTag);
        Assert.Matches(@"^Native_[A-Za-z0-9_]{10}\d+$", parsed.UniqueTag);
        Assert.Equal(string.Empty, parsed.DurableClientId);       // SubscriptionOptions.DurableClientId default
        Assert.Equal(300, parsed.DurableTimeoutSeconds);          // SubscriptionOptions.DurableTimeout default
        Assert.Equal(125, parsed.VersionOrdinal);                 // ProtocolVersion.Current
    }

    // ====================================================================
    //  Options propagation
    // ====================================================================

    [Fact]
    public void Build_propagates_dsName_from_options()
    {
        var opts = new GeodeClientOptions { Name = "test-cluster" };
        var parsed = MembershipBlob.Parse(NewBuilder(opts).Build());
        Assert.Equal("test-cluster", parsed.DsName);
    }

    [Fact]
    public void Build_propagates_durable_client_id_and_timeout()
    {
        var opts = new GeodeClientOptions();
        opts.Subscription.DurableClientId = "order-svc-1";
        opts.Subscription.DurableTimeout = TimeSpan.FromMinutes(10);

        var parsed = MembershipBlob.Parse(NewBuilder(opts).Build());

        Assert.Equal("order-svc-1", parsed.DurableClientId);
        Assert.Equal(600, parsed.DurableTimeoutSeconds);
    }

    [Fact]
    public void Build_writes_durable_fields_unconditionally_when_id_is_empty()
    {
        // Regression guard: the blob must contain durableClientId="" + 300s
        // even for non-durable clients. Skipping these fields was the
        // initial bug that caused server "Unknown header byte 0".
        var parsed = MembershipBlob.Parse(NewBuilder().Build());
        Assert.Equal(string.Empty, parsed.DurableClientId);
        Assert.Equal(300, parsed.DurableTimeoutSeconds);
    }

    // ====================================================================
    //  Process-scoped uniqueTag identity
    // ====================================================================

    [Fact]
    public void Build_two_instances_share_the_same_uniqueTag()
    {
        var a = MembershipBlob.Parse(NewBuilder().Build());
        var b = MembershipBlob.Parse(NewBuilder().Build());
        Assert.Equal(a.UniqueTag, b.UniqueTag);
    }

    [Fact]
    public void Build_uses_current_process_id()
    {
        var parsed = MembershipBlob.Parse(NewBuilder().Build());
        Assert.Equal(Environment.ProcessId, parsed.VmPid);
    }

    // ====================================================================
    //  Parser — walks the blob per the documented schema and asserts
    //  the cursor consumes the whole input.
    // ====================================================================

    private sealed record MembershipBlob(
        byte FixedIdByte,
        byte Dsfid,
        byte[] HostAddress,
        int SyncCounter,
        string Hostname,
        sbyte SplitBrainFlag,
        int DcPort,
        int VmPid,
        sbyte VmKind,
        int RoleArrayLen,
        string DsName,
        string UniqueTag,
        string DurableClientId,
        int DurableTimeoutSeconds,
        short VersionOrdinal)
    {
        public static MembershipBlob Parse(ReadOnlySpan<byte> bytes)
        {
            var pos = 0;
            var fixedIdByte = bytes[pos++];
            var dsfid = bytes[pos++];
            var hostAddress = ReadBytesVarintPrefixed(bytes, ref pos);
            var syncCounter = ReadInt32(bytes, ref pos);
            var hostname = ReadString(bytes, ref pos);
            var splitBrainFlag = (sbyte)bytes[pos++];
            var dcPort = ReadInt32(bytes, ref pos);
            var vmPid = ReadInt32(bytes, ref pos);
            var vmKind = (sbyte)bytes[pos++];
            var roleArrayLen = ReadVarintLen(bytes, ref pos);
            var dsName = ReadString(bytes, ref pos);
            var uniqueTag = ReadString(bytes, ref pos);
            var durableClientId = ReadString(bytes, ref pos);
            var durableTimeout = ReadInt32(bytes, ref pos);
            var versionOrdinal = ReadProtocolVersion(bytes, ref pos);

            Assert.Equal(bytes.Length, pos);

            return new MembershipBlob(
                fixedIdByte, dsfid, hostAddress, syncCounter, hostname,
                splitBrainFlag, dcPort, vmPid, vmKind, roleArrayLen,
                dsName, uniqueTag, durableClientId, durableTimeout, versionOrdinal);
        }

        private static int ReadVarintLen(ReadOnlySpan<byte> b, ref int pos)
        {
            var first = (sbyte)b[pos++];
            if (first == -1) return -1;
            if (first == -2)
            {
                var v = BinaryPrimitives.ReadUInt16BigEndian(b[pos..]);
                pos += 2;
                return v;
            }
            if (first == -3)
            {
                var v = BinaryPrimitives.ReadInt32BigEndian(b[pos..]);
                pos += 4;
                return v;
            }
            return first;
        }

        private static int ReadInt32(ReadOnlySpan<byte> b, ref int pos)
        {
            var v = BinaryPrimitives.ReadInt32BigEndian(b.Slice(pos, 4));
            pos += 4;
            return v;
        }

        private static byte[] ReadBytesVarintPrefixed(ReadOnlySpan<byte> b, ref int pos)
        {
            var len = ReadVarintLen(b, ref pos);
            if (len <= 0) return [];
            var result = b.Slice(pos, len).ToArray();
            pos += len;
            return result;
        }

        private static string ReadString(ReadOnlySpan<byte> b, ref int pos)
        {
            var dsCode = b[pos++];
            return dsCode switch
            {
                // CacheableASCIIString = 87 → u16 length + ASCII bytes
                87 => ReadAscii(b, ref pos),
                // CacheableString = 42 → u16 byte-length + modified UTF-8.
                // Tests only feed ASCII strings, so standard UTF-8 decode
                // is byte-equivalent for the cases we cover.
                42 => ReadModUtf8AsAscii(b, ref pos),
                // CacheableNullString = 69 → no body.
                69 => null!,
                _  => throw new InvalidOperationException(
                    $"Unexpected string DSCode {dsCode} at position {pos - 1}; " +
                    "either the writer mis-emitted a string or the schema drifted."),
            };
        }

        private static string ReadAscii(ReadOnlySpan<byte> b, ref int pos)
        {
            var len = BinaryPrimitives.ReadUInt16BigEndian(b.Slice(pos, 2));
            pos += 2;
            var s = Encoding.ASCII.GetString(b.Slice(pos, len));
            pos += len;
            return s;
        }

        private static string ReadModUtf8AsAscii(ReadOnlySpan<byte> b, ref int pos)
        {
            var byteLen = BinaryPrimitives.ReadUInt16BigEndian(b.Slice(pos, 2));
            pos += 2;
            var s = Encoding.UTF8.GetString(b.Slice(pos, byteLen));
            pos += byteLen;
            return s;
        }

        private static short ReadProtocolVersion(ReadOnlySpan<byte> b, ref int pos)
        {
            var first = (sbyte)b[pos++];
            // Compressed form (ordinal ≤ 127) — single byte.
            if (first != -1) return first;
            // Uncompressed: sentinel + i16 ordinal.
            var ordinal = BinaryPrimitives.ReadInt16BigEndian(b.Slice(pos, 2));
            pos += 2;
            return ordinal;
        }
    }
}
