using System.Buffers;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Geode.Client.Options;
using Microsoft.Extensions.Options;

namespace Geode.Client.Protocol;

/// <summary>
/// Generates the inner identity blob of a Geode <c>ClientProxyMembershipID</c>
/// — i.e. the bytes carried in step 6c of the handshake. Mirrors cppcache
/// <c>ClientProxyMembershipIDFactory::create</c> +
/// <c>ClientProxyMembershipID::initObjectVars</c>.
/// </summary>
/// <remarks>
/// <para>
/// The blob is a serialised Java <c>InternalDistributedMember</c>
/// (DataSerializableFixedID = 92), <b>not</b> a serialised
/// <c>ClientProxyMembershipID</c>. The outer ClientProxyMembershipID
/// framing (FixedIDByte + DSFid 38 + identity blob + i32 uniqueId) is
/// added by <c>TcrConnection.HandshakeAsync</c> step 6 — this builder only
/// emits the identity bytes.
/// </para>
/// <para>
/// Should be registered as a singleton (Phase 5 DI). All connections in a
/// process share the same identity — matches cppcache where one factory
/// per process holds a single <c>randString_</c> reused across every
/// <c>create()</c>. The result is cached after the first <see cref="Build"/>
/// call since inputs (hostname, IP, PID, options) are immutable.
/// </para>
/// </remarks>
internal sealed class ClientProxyMembershipIdBuilder(IOptions<GeodeClientOptions> options)
{
    // === cppcache hardcoded values (ClientProxyMembershipID.cpp:31-33) ======
    private const byte FixedIdByte = 1;
    private const byte InternalDistributedMemberDsfid = 92;
    private const sbyte VmKindLoner = 13;
    private const int DcPort = 12334;

    /// <summary>
    /// Process-scoped unique tag, generated once at type-load. cppcache
    /// builds this in the factory constructor as
    /// <c>"Native_" + 10 random alphanumerics + ProcessId</c>.
    /// </summary>
    private static readonly string s_uniqueTag = GenerateUniqueTag();

    private readonly GeodeClientOptions _options = options.Value;

    /// <summary>
    /// Cached identity bytes. Inputs are immutable for the lifetime of this
    /// builder, so we compute once and reuse on subsequent calls.
    /// </summary>
    private byte[]? _identity;

    /// <summary>
    /// Build the identity blob. Idempotent — repeated calls return the same
    /// byte array reference.
    /// </summary>
    public byte[] Build()
    {
        if (_identity is not null)
        {
            return _identity;
        }

        var buffer = new ArrayBufferWriter<byte>();
        var w = new BigEndianBinaryWriter(buffer);

        // Outer framing: this is a serialised InternalDistributedMember.
        w.WriteByte(FixedIdByte);
        w.WriteByte(InternalDistributedMemberDsfid);

        // Host address: raw IP bytes (4 for IPv4, 16 for IPv6) prefixed
        // with varint length via WriteBytes.
        w.WriteBytes(ResolveHostAddress());

        // SyncCounter — reconnect counter; fresh process = 0.
        w.WriteInt32(0);

        // Hostname — DSCode-tagged string (server reads via
        // StaticSerialization.readString).
        w.WriteString(Dns.GetHostName());

        // SplitBrainFlag — false. cppcache hardcodes 0 in the relevant ctor.
        w.WriteSByte(0);

        // DcPort — distributed-cache port; cppcache hardcodes 12334.
        w.WriteInt32(DcPort);

        // vPID — process ID, lets the server distinguish co-tenant clients.
        w.WriteInt32(Environment.ProcessId);

        // vmKind = LONER (13) — we are not a Geode peer / locator / admin.
        w.WriteSByte(VmKindLoner);

        // RoleArrayLength — no roles. Varint encoding (matches server's
        // StaticSerialization.readStringArray length sentinel for empty/null).
        w.WriteArrayLen(0);

        // dsName — distributed system name; usually "" for clients.
        w.WriteString(_options.Name);

        // uniqueTag — randomly generated per process.
        w.WriteString(s_uniqueTag);

        // Durable subscription metadata. Server's MemberIdentifierImpl.toData
        // / fromDataPre_GFE_9_0_0_0 reads BOTH unconditionally, so we must
        // write them every time:
        //   - empty string + 300 (server's documented default) for non-durable
        //   - configured values for durable
        // The previous "if (durable) throw; else skip" path corrupted the
        // wire because the server then read the trailing Version bytes as
        // string contents, hitting "Unknown header byte 0".
        var sub = _options.Subscription;
        w.WriteString(sub.DurableClientId);
        w.WriteInt32((int)sub.DurableTimeout.TotalSeconds);

        // Trailing protocol-version stamp (compressed ordinal).
        ProtocolVersion.Current.WriteTo(w);

        _identity = buffer.WrittenSpan.ToArray();
        return _identity;
    }

    /// <summary>
    /// Resolve the local hostname's first IP and return its raw bytes
    /// (4 for IPv4, 16 for IPv6). Mirrors cppcache's
    /// <c>resolver.resolve(hostname, "0")</c> followed by taking the first
    /// endpoint's address — no filtering by family.
    /// </summary>
    private static byte[] ResolveHostAddress()
    {
        var hostname = Dns.GetHostName();
        var addresses = Dns.GetHostAddresses(hostname);
        if (addresses.Length == 0)
        {
            throw new InvalidOperationException(
                $"No IP address resolved for local hostname '{hostname}'.");
        }
        return addresses[0].GetAddressBytes();
    }

    /// <summary>
    /// Generate the process-scoped unique tag. Format matches cppcache
    /// <c>ClientProxyMembershipIDFactory</c> ctor exactly so server-side
    /// log scraping / tooling is interchangeable.
    /// </summary>
    private static string GenerateUniqueTag()
    {
        const string alphabet =
            "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_";

        var sb = new StringBuilder(capacity: 7 + 10 + 10);
        sb.Append("Native_");
        for (int i = 0; i < 10; i++)
        {
            sb.Append(alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]);
        }
        sb.Append(Environment.ProcessId);
        return sb.ToString();
    }
}
