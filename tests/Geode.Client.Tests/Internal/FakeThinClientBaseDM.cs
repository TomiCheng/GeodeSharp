using Geode.Client.Internal;
using Geode.Client.Protocol;

namespace Geode.Client.Tests.Internal;

/// <summary>
/// Test fake for <see cref="ThinClientBaseDM"/>: captures the outbound
/// <see cref="TcrMessage"/> in <see cref="LastRequest"/> and serves a
/// caller-configured <see cref="CannedReply"/>. The other three
/// <c>Send*</c> abstract methods all throw <see cref="NotSupportedException"/>
/// so a test that hits the wrong overload flags immediately.
/// </summary>
// sp param kept so the existing ~70 test call sites compile unchanged; the
// base ctor no longer accepts it, so it's unused here.
#pragma warning disable CS9113
internal sealed class FakeThinClientBaseDM(IServiceProvider sp, GeodeCache cache)
    : ThinClientBaseDM(cache)
#pragma warning restore CS9113
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

    /// <summary>Chunks staged here are fed to <c>HandleChunk</c> in order before returning the canned reply.</summary>
    public List<ReadOnlyMemory<byte>> StagedChunks { get; } = [];

    public override Task<TcrMessage> SendSyncRequestAsync(
        TcrMessage request, TcrChunkedResult chunkedResult,
        bool attemptFailover = true, bool isBackgroundThread = false,
        CancellationToken ct = default)
    {
        LastRequest = request;
        chunkedResult.Reset();
        for (var i = 0; i < StagedChunks.Count; i++)
        {
            var isLast = i == StagedChunks.Count - 1;
            chunkedResult.HandleChunk(StagedChunks[i], isLast);
        }
        return Task.FromResult(CannedReply
            ?? throw new InvalidOperationException("CannedReply not set."));
    }

    public override Task<TcrMessage> SendRequestToEndpointAsync(
        TcrMessage request, TcrEndpoint endpoint, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public override Task<TcrMessage> SendRequestToEndpointAsync(
        TcrMessage request, TcrChunkedResult chunkedResult,
        TcrEndpoint endpoint, CancellationToken ct = default) =>
        throw new NotSupportedException();
}
