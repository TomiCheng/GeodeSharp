namespace Geode.Client.Tests;

/// <summary>
/// Wraps a test body so <see cref="NotConnectedException"/> counts as a
/// soft pass. Unit tests verify the wire pipeline reaches the
/// server-handoff point; when no server is up, the wire layer surfaces
/// <c>NotConnectedException</c> (cppcache <c>GF_NOTCON</c> parity) and we
/// treat that as "wiring OK". Hard-asserting wire round-trips belongs in
/// <c>Geode.Client.IntegrationTests</c> (Testcontainers Geode fixture).
/// </summary>
/// <remarks>
/// Use only on tests whose body actually touches the wire. Local-only
/// shortcuts (<c>Local</c> / <c>LocalEntryLru</c>) don't need this — they
/// never throw <c>NotConnectedException</c> in the first place; wrapping
/// them would hide real bugs.
/// </remarks>
public static class ServerOptional
{
    /// <summary>
    /// Run <paramref name="body"/>; swallow <see cref="NotConnectedException"/>.
    /// </summary>
    public static async Task RunAsync(Func<Task> body)
    {
        try
        {
            await body();
        }
        catch (NotConnectedException)
        {
            // OK in unit-test context: wire pipeline reached the
            // server-handoff point.
        }
    }
}
