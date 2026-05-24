using Geode.Client.Internal;
using Geode.Client.Protocol;
using Geode.Client.Services;

namespace Geode.Client.Tests.Internal;

/// <summary>
/// Test fake for <see cref="ThinClientBaseDM"/>: captures the outbound
/// <see cref="TcrMessage"/> in <see cref="LastRequest"/> and serves a
/// caller-configured <see cref="CannedReply"/>. The other three
/// <c>Send*</c> abstract methods all throw <see cref="NotSupportedException"/>
/// so a test that hits the wrong overload flags immediately.
/// </summary>
internal sealed class FakeThinClientBaseDM(IServiceProvider sp, GeodeCache cache)
    : ThinClientBaseDM(sp, cache)
{
    public TcrMessage? LastRequest { get; private set; }
    public TcrMessage? CannedReply { get; set; }

    public override Task<TcrMessage> SendSyncRequestAsync(
        TcrMessage request, bool attemptFailover = true,
        bool isBackgroundThread = false, CancellationToken ct = default)
    {
        LastRequest = request;
        return Task.FromResult(CannedReply
            ?? throw new InvalidOperationException("CannedReply not set."));
    }

    public override Task<TcrMessage> SendSyncRequestAsync(
        TcrMessage request, TcrChunkedResult chunkedResult,
        bool attemptFailover = true, bool isBackgroundThread = false,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Unit-test fake — chunked overload not wired.");

    public override Task<TcrMessage> SendRequestToEndpointAsync(
        TcrMessage request, TcrEndpoint endpoint, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public override Task<TcrMessage> SendRequestToEndpointAsync(
        TcrMessage request, TcrChunkedResult chunkedResult,
        TcrEndpoint endpoint, CancellationToken ct = default) =>
        throw new NotSupportedException();
}
