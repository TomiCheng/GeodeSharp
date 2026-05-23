/*
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace Geode.Client.Internal;

/// <summary>
/// One short-lived TCP connection to a locator endpoint. Mirrors
/// cppcache <c>TcpConn</c> instantiated by
/// <c>ThinClientLocatorHelper::createConnection</c>
/// (<c>cppcache/src/ThinClientLocatorHelper.cpp:87-115</c>): open
/// socket, write one request, read one response, close.
/// </summary>
/// <remarks>
/// <para>
/// Distinct from <see cref="Geode.Client.Protocol.TcrConnection"/> —
/// no Geode handshake, no 17-byte framed header, no transaction id,
/// no connection pool. Locator protocol is one request per connection;
/// dispose between requests. Nagle is disabled (same as cppcache) so
/// the one-shot send hits the wire immediately.
/// </para>
/// <para>
/// Step C lands only plain TCP (<c>TcpConn</c> equivalent). The cppcache
/// <c>TcpSslConn</c> branch is Phase 3 TLS work; SNI is the same phase.
/// </para>
/// </remarks>
internal sealed class LocatorConnection(ILogger<LocatorConnection> logger) : IAsyncDisposable
{
    private readonly TcpClient _tcpClient = new() { NoDelay = true };
    private NetworkStream? _stream;
    private int _disposed;

    /// <summary>Open the socket to <paramref name="host"/>:<paramref name="port"/>. Caller sets connect timeout via <paramref name="ct"/>.</summary>
    public async Task ConnectAsync(string host, int port, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(host);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        await _tcpClient.ConnectAsync(host, port, ct).ConfigureAwait(false);
        _stream = _tcpClient.GetStream();
        logger.LogDebug("LocatorConnection connected to {Host}:{Port}", host, port);
    }

    /// <summary>Write <paramref name="data"/> in full and flush. Caller has already produced the bytes (gossip version + Geode version + DSCode-tagged FixedId envelope + request body).</summary>
    public async Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        var stream = _stream
            ?? throw new InvalidOperationException(
                $"{nameof(ConnectAsync)} must be called before {nameof(SendAsync)}.");

        logger.LogTrace("LocatorConnection sending {ByteCount} bytes", data.Length);
        await stream.WriteAsync(data, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Block until <paramref name="buffer"/> is filled or the peer closes.</summary>
    /// <exception cref="EndOfStreamException">Peer closed before the buffer was filled.</exception>
    public async Task ReadExactlyAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        var stream = _stream
            ?? throw new InvalidOperationException(
                $"{nameof(ConnectAsync)} must be called before {nameof(ReadExactlyAsync)}.");

        await stream.ReadExactlyAsync(buffer, ct).ConfigureAwait(false);
    }

    /// <summary>Read whatever bytes are currently available, up to <paramref name="buffer"/>.Length. Returns 0 on peer close (EOF).</summary>
    public async Task<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        var stream = _stream
            ?? throw new InvalidOperationException(
                $"{nameof(ConnectAsync)} must be called before {nameof(ReadAsync)}.");

        return await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Close the socket cleanly. Mirrors cppcache <c>TcpConn</c>
    /// destructor: flush pending writes, send a TCP FIN
    /// (<c>SocketShutdown.Both</c>) so the peer sees a graceful close
    /// instead of an RST, then release the OS handles. Idempotent.
    /// </summary>
    public async Task CloseAsync(CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        var stream = _stream;
        _stream = null;

        if (stream is not null)
        {
            // Step 1: flush anything buffered locally. Swallow errors —
            // socket may already be dead and we still want to proceed
            // with the rest of the teardown.
            try
            {
                await stream.FlushAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogTrace(ex, "LocatorConnection: flush before close failed (benign)");
            }

            // Step 2: half-close both directions. Sends a FIN so the
            // peer's read loop returns 0 cleanly; without this the
            // underlying TcpClient.Dispose can leave the socket in
            // TIME_WAIT with RST behaviour on some stacks.
            try
            {
                _tcpClient.Client.Shutdown(SocketShutdown.Both);
            }
            catch (Exception ex)
            {
                logger.LogTrace(ex, "LocatorConnection: socket shutdown failed (benign — socket likely already closed)");
            }

            stream.Dispose();
        }

        _tcpClient.Dispose();
        logger.LogTrace("LocatorConnection closed");
    }

    public ValueTask DisposeAsync() => new(CloseAsync());
}

*/