using System.Text;
using Geode.Client.Internal;
using Geode.Client.Options;
using Geode.Client.Protocol;
using Geode.Client.Protocol.Serialization;
using Microsoft.Extensions.Logging;

namespace Geode.Client.Services;

/// <summary>
/// Concrete proxy-mode region implementation. Mirrors cppcache
/// <c>ThinClientRegion</c>
/// (<c>cppcache/src/ThinClientRegion.hpp:51</c>): inherits the local
/// machinery (here: empty placeholder
/// <see cref="LocalRegion"/> / <see cref="RegionInternal"/>) and adds
/// server roundtrips via a <see cref="ThinClientBaseDM"/>.
/// </summary>
/// <remarks>
/// <para>
/// Phase 1.2 skeleton: fields + ctor in place, all 4 IRegion ops
/// throw <see cref="NotImplementedException"/>. Wire dispatch
/// (<c>SendSyncRequestAsync</c> via <see cref="ThinClientBaseDM"/>)
/// lands in Phase 1.2.e alongside the operation builders and the
/// reply decoder.
/// </para>
/// <para>
/// Note the type is non-generic — <c>TKey, TValue</c> live only on
/// the public <see cref="IRegion{TKey, TValue}"/> view, exposed
/// through <see cref="RegionView{TKey, TValue}"/>. The wire path
/// is <c>object</c>-typed; strong typing is compile-time only.
/// </para>
/// </remarks>
internal sealed class ThinClientRegion(
    ILogger<ThinClientRegion> logger,
    TcrMessageBuilder tcrMessageBuilder,
    SerializationRegistry serializationRegistry,
    string name,
    CacheXmlRegionAttributesOptions attributes,
    ThinClientBaseDM dm)
    : LocalRegion(name, null, attributes)
{

    /// <summary>
    /// Distribution manager this region dispatches to. Mirrors
    /// cppcache <c>ThinClientRegion::m_tcrdm</c>; pool-mode MVP
    /// always carries a <see cref="ThinClientPoolDM"/> here.
    /// </summary>
    internal ThinClientBaseDM DistributionManager => dm;

    public override Task PutAsync(object key, object value, CancellationToken ct = default)
    {
        // TODO Phase 1.2.e: build TcrMessageBuilder.Put(...) with this
        //   region's FullPath, dispatch via _dm.SendSyncRequestAsync,
        //   inspect reply.MessageType (Reply OK / Exception → throw).
        //   Mirrors cppcache ThinClientRegion::putNoThrow_remote
        //   (ThinClientRegion.cpp).
        throw new NotImplementedException("TODO Phase 1.2.e: ThinClientRegion.PutAsync");
    }

    public override Task<object?> GetAsync(object key, CancellationToken ct = default)
    {
        // TODO Phase 1.2.e: TcrMessageBuilder.Get(FullPath, key) →
        //   _dm.SendSyncRequestAsync → decode Response (DSCode-aware).
        //   Mirrors cppcache ThinClientRegion::getNoThrow_remote.
        throw new NotImplementedException("TODO Phase 1.2.e: ThinClientRegion.GetAsync");
    }

    public override Task<bool> RemoveAsync(object key, CancellationToken ct = default)
    {
        // TODO Phase 1.2.e: TcrMessageBuilder.Destroy(FullPath, key) →
        //   _dm.SendSyncRequestAsync → reply means key existed; absent-key
        //   surfaces as a specific Exception subtype.
        //   Mirrors cppcache ThinClientRegion::destroyNoThrow_remote.
        throw new NotImplementedException("TODO Phase 1.2.e: ThinClientRegion.RemoveAsync");
    }

    public override async Task<bool> ContainsKeyAsync(object key, CancellationToken ct = default)
    {
        logger.LogTrace("ContainsKeyAsync: region={RegionPath}, key={Key}", FullPath, key);

        // Mirrors cppcache ThinClientRegion::containsKeyOnServer
        // (cppcache/src/ThinClientRegion.cpp:676-720) +
        // TcrMessageContainsKey ctor (TcrMessage.cpp:1808-1843).
        //
        // ─── Step 1+2: build request frame ────────────────────
        // Region FullPath + DSCode-tagged key via
        // SerializationRegistry; partial source:
        // Protocol/TcrMessageBuilder.ContainsKey.cs.
        var request = tcrMessageBuilder.ContainsKey(FullPath, key);

        // ─── Step 3: dispatch via DM ─────────────────────────
        // ThinClientPoolDM.SendSyncRequestAsync picks the (single in
        // MVP) endpoint, routes through SendRequestToEndpointAsync
        // (conn borrow / fallback create / send / put-back).
        var reply = await dm
            .SendSyncRequestAsync(request, ct: ct)
            .ConfigureAwait(false);

        // ─── Step 4: reply decoding ──────────────────────────
        // cppcache containsKeyOnServer reply switch
        // (ThinClientRegion.cpp:691-712): Response → bool, Exception
        // → throw, anything else → throw.
        switch (reply.MessageType)
        {
            case MessageType.Response:
                {
                    // Part 0 payload = [DSCode.CacheableBoolean][0/1].
                    // Registry consumes the DSCode and dispatches to
                    // BooleanDataConverter for the 1-byte body.
                    var partReader = new BigEndianBinaryReader(reply.Parts[0].Payload);
                    var value = serializationRegistry.ReadObject(partReader);
                    if (value is bool b)
                    {
                        return b;
                    }
                    throw new GeodeException(
                        $"ContainsKey on '{FullPath}': expected bool reply, " +
                        $"got {value?.GetType().Name ?? "null"}.");
                }

            case MessageType.Exception:
                throw new GeodeException(
                    $"Server exception on ContainsKey '{FullPath}': " +
                    DecodeExceptionPreview(reply));

            default:
                throw new GeodeException(
                    $"Unexpected reply type {reply.MessageType} for ContainsKey on '{FullPath}'.");
        }
    }

    /// <summary>
    /// Best-effort ASCII preview of an Exception reply's Part 0. The
    /// server typically returns the Java exception class name +
    /// message there as a <c>CacheableASCIIString</c>; until
    /// <c>StringDataConverter</c> lands we just render printable bytes
    /// directly so the caller sees a readable hint in the
    /// <see cref="GeodeException"/> message. Mirrors the diagnostic
    /// pattern in <c>GetDiagnosticTests</c>.
    /// </summary>
    private static string DecodeExceptionPreview(TcrMessage reply)
    {
        if (reply.Parts.Count == 0)
        {
            return "<no exception parts>";
        }

        var bytes = reply.Parts[0].Payload.Span;
        var sb = new StringBuilder(bytes.Length);
        foreach (var b in bytes)
        {
            sb.Append(b is >= 0x20 and < 0x7F ? (char)b : '.');
        }
        return sb.ToString();
    }
}
